using System.Text.Json;
using Hms.Database.Entities.Platform.Workflows;
using Hms.Database.Repositories;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

/// <summary>
/// Loads SDLC workflow definitions from DB and drives stage-based execution.
/// When a project has a DB-backed workflow, this replaces the hardcoded dependency DAG.
/// </summary>
public sealed class WorkflowExecutionEngine : IWorkflowExecutionEngine
{
    private readonly IPlatformRepository<SdlcWorkflow> _workflowRepo;
    private readonly IPlatformRepository<StageDefinition> _stageRepo;
    private readonly IPlatformRepository<ApprovalGateConfig> _gateRepo;
    private readonly IHumanGate _humanGate;
    private readonly ILogger<WorkflowExecutionEngine> _logger;

    // Track approved gates per run: runId -> set of stageIds
    private readonly Dictionary<string, HashSet<string>> _approvedGates = new();

    public WorkflowExecutionEngine(
        IPlatformRepository<SdlcWorkflow> workflowRepo,
        IPlatformRepository<StageDefinition> stageRepo,
        IPlatformRepository<ApprovalGateConfig> gateRepo,
        IHumanGate humanGate,
        ILogger<WorkflowExecutionEngine> logger)
    {
        _workflowRepo = workflowRepo;
        _stageRepo = stageRepo;
        _gateRepo = gateRepo;
        _humanGate = humanGate;
        _logger = logger;
    }

    public async Task LoadWorkflowAsync(AgentContext context, string? workflowId, CancellationToken ct = default)
    {
        SdlcWorkflow? workflow = null;

        if (!string.IsNullOrEmpty(workflowId))
            workflow = await _workflowRepo.GetByIdAsync(workflowId, ct);

        // Fallback to default workflow
        if (workflow is null)
        {
            var defaults = await _workflowRepo.QueryAsync(w => w.IsDefault, take: 1, ct: ct);
            workflow = defaults.FirstOrDefault();
        }

        if (workflow is null)
        {
            _logger.LogWarning("No workflow found (id={WorkflowId}). Pipeline will use hardcoded dependency DAG.", workflowId);
            return;
        }

        context.WorkflowId = workflow.Id;

        // Load stages ordered by position
        var stages = await _stageRepo.QueryAsync(
            s => s.WorkflowId == workflow.Id,
            take: 200,
            ct: ct);
        stages = stages.OrderBy(s => s.Order).ToList();

        // Load all approval gates for these stages
        var stageIds = stages.Select(s => s.Id).ToHashSet();
        var allGates = await _gateRepo.ListAsync(take: 500, ct: ct);
        var gatesByStage = allGates
            .Where(g => stageIds.Contains(g.StageId))
            .GroupBy(g => g.StageId)
            .ToDictionary(g => g.Key, g => g.First());

        var resolved = new List<ResolvedStage>();
        foreach (var stage in stages)
        {
            var agentTypes = ParseAgentTypes(stage.AgentsInvolvedJson);
            ResolvedApprovalGate? gate = null;

            if (gatesByStage.TryGetValue(stage.Id, out var gateConfig))
            {
                gate = new ResolvedApprovalGate
                {
                    GateType = gateConfig.GateType,
                    ApproversConfigJson = gateConfig.ApproversConfigJson,
                    TimeoutHours = gateConfig.TimeoutHours
                };
            }

            resolved.Add(new ResolvedStage
            {
                StageId = stage.Id,
                Name = stage.Name,
                Order = stage.Order,
                AgentsInvolved = agentTypes,
                EntryCriteria = stage.EntryCriteria,
                ExitCriteria = stage.ExitCriteria,
                ApprovalGate = gate
            });
        }

        context.ResolvedStages = resolved;
        _logger.LogInformation("Loaded workflow '{Name}' with {Count} stages for run {RunId}",
            workflow.Name, resolved.Count, context.RunId);
    }

    public IReadOnlyList<AgentType> GetReadyAgents(AgentContext context, IReadOnlySet<AgentType> completedAgents)
    {
        if (context.ResolvedStages.Count == 0)
            return [];

        var ready = new List<AgentType>();

        foreach (var stage in context.ResolvedStages.OrderBy(s => s.Order))
        {
            // Check if prior stages are complete
            var priorStages = context.ResolvedStages
                .Where(s => s.Order < stage.Order)
                .ToList();

            var priorComplete = priorStages.All(ps => IsStageComplete(context, ps, completedAgents));
            if (!priorComplete) continue;

            // Check entry criteria
            if (!EvaluateEntryCriteria(context, stage, completedAgents)) continue;

            // Agents in this stage that haven't completed yet
            var pending = stage.AgentsInvolved
                .Where(a => !completedAgents.Contains(a))
                .ToList();

            ready.AddRange(pending);
        }

        return ready;
    }

    public bool IsStageComplete(AgentContext context, ResolvedStage stage, IReadOnlySet<AgentType> completedAgents)
    {
        // All agents in the stage must have completed
        if (!stage.AgentsInvolved.All(completedAgents.Contains))
            return false;

        // Check exit criteria if defined
        return EvaluateExitCriteria(context, stage);
    }

    public async Task<bool> IsApprovalRequiredAsync(AgentContext context, ResolvedStage stage, CancellationToken ct = default)
    {
        if (stage.ApprovalGate is null)
            return false;

        if (stage.ApprovalGate.GateType == "auto")
            return false;

        // Check if already approved for this run
        if (_approvedGates.TryGetValue(context.RunId, out var approved) && approved.Contains(stage.StageId))
            return false;

        return true;
    }

    public Task ApproveGateAsync(AgentContext context, string stageId, string approver, CancellationToken ct = default)
    {
        if (!_approvedGates.TryGetValue(context.RunId, out var approved))
        {
            approved = [];
            _approvedGates[context.RunId] = approved;
        }

        approved.Add(stageId);
        _logger.LogInformation("Approval gate for stage {StageId} approved by {Approver} in run {RunId}",
            stageId, approver, context.RunId);

        return Task.CompletedTask;
    }

    // ── Private helpers ──

    private static List<AgentType> ParseAgentTypes(string json)
    {
        var result = new List<AgentType>();
        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            foreach (var name in names)
            {
                if (Enum.TryParse<AgentType>(name, ignoreCase: true, out var at))
                    result.Add(at);
            }
        }
        catch
        {
            // Malformed JSON — return empty
        }
        return result;
    }

    private static bool EvaluateEntryCriteria(AgentContext context, ResolvedStage stage, IReadOnlySet<AgentType> completedAgents)
    {
        if (string.IsNullOrEmpty(stage.EntryCriteria))
            return true;

        try
        {
            var criteria = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stage.EntryCriteria);
            if (criteria is null) return true;

            // "requires_agents": ["RequirementsReader","Architect"] — all listed agents must be complete
            if (criteria.TryGetValue("requires_agents", out var reqAgents) && reqAgents.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in reqAgents.EnumerateArray())
                {
                    if (Enum.TryParse<AgentType>(elem.GetString(), ignoreCase: true, out var at)
                        && !completedAgents.Contains(at))
                        return false;
                }
            }

            // "min_artifacts": 5 — context must have at least N artifacts
            if (criteria.TryGetValue("min_artifacts", out var minArt) && minArt.ValueKind == JsonValueKind.Number)
            {
                if (context.Artifacts.Count < minArt.GetInt32())
                    return false;
            }

            // "min_requirements": 1 — context must have at least N expanded requirements
            if (criteria.TryGetValue("min_requirements", out var minReq) && minReq.ValueKind == JsonValueKind.Number)
            {
                if (context.ExpandedRequirements.Count < minReq.GetInt32())
                    return false;
            }

            return true;
        }
        catch
        {
            return true; // malformed criteria → treat as met
        }
    }

    private static bool EvaluateExitCriteria(AgentContext context, ResolvedStage stage)
    {
        if (string.IsNullOrEmpty(stage.ExitCriteria))
            return true;

        try
        {
            var criteria = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stage.ExitCriteria);
            if (criteria is null) return true;

            // "all_tests_pass": true — no test diagnostics with Failed outcome
            if (criteria.TryGetValue("all_tests_pass", out var atp) && atp.ValueKind == JsonValueKind.True)
            {
                if (context.TestDiagnostics.Any(d => d.Outcome == HmsAgents.Core.Models.TestOutcome.Failed))
                    return false;
            }

            // "max_critical_findings": 0 — no critical findings
            if (criteria.TryGetValue("max_critical_findings", out var mcf) && mcf.ValueKind == JsonValueKind.Number)
            {
                var criticalCount = context.Findings.Count(f =>
                    f.Severity == HmsAgents.Core.Enums.ReviewSeverity.Critical);
                if (criticalCount > mcf.GetInt32())
                    return false;
            }

            // "coverage_min": 80 — placeholder (not enforced without external coverage data)

            return true;
        }
        catch
        {
            return true;
        }
    }
}
