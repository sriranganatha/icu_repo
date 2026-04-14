using System.Collections.Concurrent;
using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Extensions;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using GNex.Database.Entities.Platform.AgentRegistry;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Orchestrator;

/// <summary>
/// Daemon-style orchestrator. Runs as a coordinator loop that dispatches
/// parallel waves of agents based on dependency readiness.
/// Supports mid-pipeline requirement injection, background Review, backlog tracking,
/// and inter-agent directive messaging.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IEnumerable<IAgent> _agents;
    private readonly ILlmProvider _llm;
    private readonly IArtifactWriter _writer;
    private readonly IPipelineEventSink _eventSink;
    private readonly IAuditLogger _audit;
    private readonly IHumanGate _humanGate;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentOrchestrator> _logger;
    private AgentContext? _current;

    // ── Concurrent project pipelines ──
    private readonly ConcurrentDictionary<string, AgentContext> _activeContexts = new();

    private const int MaxAgentRetries = 2;
    private const int MaxClaimBatchSize = 50;
    private const int MaxAdaptiveWipCap = 50;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DaemonPollInterval = TimeSpan.FromMilliseconds(500);

    // ─── Dependency graph: agent → set of agents it must wait for ───
    private static readonly Dictionary<AgentType, AgentType[]> s_dependencies = new()
    {
        [AgentType.RequirementsReader]    = [],
        [AgentType.PromptGenerator]       = [AgentType.RequirementsReader],
        [AgentType.Architect]             = [AgentType.RequirementsReader],
        [AgentType.PlatformBuilder]       = [AgentType.Architect],
        [AgentType.RequirementsExpander]  = [AgentType.RequirementsReader, AgentType.Architect, AgentType.PromptGenerator],
        [AgentType.Backlog]               = [AgentType.RequirementsExpander],
        // Code-gen agents depend only on PlatformBuilder (not Backlog) because Backlog
        // cycles continuously. First dispatch is gated by backlogRanAtLeastOnce flag;
        // subsequent dispatches use the backlog-driven re-dispatch block.
        [AgentType.Database]              = [AgentType.PlatformBuilder],
        [AgentType.ServiceLayer]          = [AgentType.PlatformBuilder],
        [AgentType.Application]           = [AgentType.PlatformBuilder],
        [AgentType.Integration]           = [AgentType.PlatformBuilder],
        [AgentType.Testing]               = [AgentType.Database, AgentType.ServiceLayer, AgentType.PlatformBuilder],
        [AgentType.Review]               = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration, AgentType.Testing],
        // Enrichment — need Requirements + DomainProfile
        [AgentType.Security]             = [AgentType.RequirementsReader, AgentType.PromptGenerator],
        [AgentType.HipaaCompliance]      = [AgentType.RequirementsReader, AgentType.PromptGenerator],
        [AgentType.Soc2Compliance]       = [AgentType.RequirementsReader, AgentType.PromptGenerator],
        [AgentType.AccessControl]        = [AgentType.RequirementsReader, AgentType.PromptGenerator],
        [AgentType.Observability]        = [AgentType.RequirementsReader, AgentType.PromptGenerator],
        [AgentType.Infrastructure]       = [AgentType.Architect],
        [AgentType.ApiDocumentation]     = [AgentType.RequirementsReader, AgentType.PromptGenerator],
        [AgentType.Performance]          = [AgentType.RequirementsReader],
        // Remediation — after Review
        [AgentType.BugFix]               = [AgentType.Review],
        // Supervisor — last, after everything
        [AgentType.Supervisor]           = [],  // handled specially as final gate
        // Deploy — on-demand, after all core + infra agents
        [AgentType.Deploy]               = [AgentType.Review, AgentType.Testing, AgentType.Infrastructure],
        // Requirement Analyzer — runs after code generation cycle to find gaps
        [AgentType.RequirementAnalyzer]  = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration, AgentType.Review],
        // Build — compiles the generated solution after all code-gen + review
        [AgentType.Build]                = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration, AgentType.Testing, AgentType.Review, AgentType.BugFix],
        // Monitor — runs after Deploy to check containers and service health
        [AgentType.Monitor]              = [AgentType.Deploy],
        // GapAnalysis — runs after code generation + review + analyzer to find implementation gaps
        [AgentType.GapAnalysis]          = [AgentType.RequirementAnalyzer, AgentType.Integration, AgentType.Review],
        // Planning — reasoning agent that creates implementation plans before code-gen
        [AgentType.Planning]             = [AgentType.RequirementsExpander, AgentType.Architect],
        // CodeReasoning — holistic post-generation analysis before Review
        [AgentType.CodeReasoning]        = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration],
        // Migration — after Database to handle schema migrations
        [AgentType.Migration]            = [AgentType.Database],
        // CodeQuality — after all code-gen
        [AgentType.CodeQuality]          = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration],
        // DependencyAudit — after PlatformBuilder sets up project references
        [AgentType.DependencyAudit]      = [AgentType.PlatformBuilder],
        // Refactoring — after Review identifies refactoring opportunities
        [AgentType.Refactoring]          = [AgentType.Review],
        // Configuration — after PlatformBuilder sets up project structure
        [AgentType.Configuration]        = [AgentType.PlatformBuilder],
        // UiUx — after Application layer generates endpoints
        [AgentType.UiUx]                 = [AgentType.Application],
        // LoadTest — after Deploy for performance validation
        [AgentType.LoadTest]             = [AgentType.Deploy],
        // DodVerification — quality gate after all code-gen + Review complete
        [AgentType.DodVerification]      = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration, AgentType.Testing, AgentType.Review],
        // BRD generation — after requirements are expanded
        [AgentType.BrdGenerator]         = [AgentType.RequirementsExpander, AgentType.PromptGenerator],
        // Conflict resolution — after all code-gen agents
        [AgentType.ConflictResolver]     = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration],
        // Traceability gate — after Review + Testing
        [AgentType.TraceabilityGate]     = [AgentType.Review, AgentType.Testing],
        // Sprint planning — after requirements are expanded
        [AgentType.SprintPlanner]        = [AgentType.RequirementsExpander],
        // Learning loop — runs last, after everything
        [AgentType.LearningLoop]         = [],  // handled as meta/final agent
    };

    // Finding → remediation dispatch
    private static readonly Dictionary<string, AgentType[]> s_findingDispatch = new()
    {
        ["NFR-CODE-01"]        = [AgentType.BugFix],
        ["NFR-CODE-02"]        = [AgentType.BugFix],
        ["NFR-TEST-01"]        = [AgentType.BugFix],
        ["Implementation"]     = [AgentType.BugFix, AgentType.ServiceLayer],
        ["MultiTenant"]        = [AgentType.BugFix, AgentType.Database],
        ["Audit"]              = [AgentType.BugFix],
        ["Security"]           = [AgentType.BugFix, AgentType.Security],
        ["Traceability"]       = [AgentType.BugFix],
        ["Conventions"]        = [AgentType.BugFix],
        ["Coverage"]           = [AgentType.BugFix],
        ["FeatureCoverage"]    = [AgentType.BugFix],
        ["TestCoverage"]       = [AgentType.BugFix, AgentType.Testing],
        ["Performance"]        = [AgentType.Performance],
        ["Performance-N+1"]    = [AgentType.Performance],
        ["Performance-EF"]     = [AgentType.Performance],
        ["OWASP-A01"]          = [AgentType.Security],
        ["OWASP-A02"]          = [AgentType.Security],
        ["OWASP-A03"]          = [AgentType.Security],
        ["HIPAA-164.312(a)"]   = [AgentType.HipaaCompliance],
        ["HIPAA-164.312(b)"]   = [AgentType.HipaaCompliance],
        ["SOC2-CC6"]           = [AgentType.Soc2Compliance],
        ["SOC2-CC7"]           = [AgentType.Soc2Compliance],
        ["SOC2-CC8"]           = [AgentType.Soc2Compliance],
        // Build/Deploy/Monitor/Database findings → BugFix remediation
        ["Build"]              = [AgentType.BugFix],
        ["Deployment"]         = [AgentType.BugFix],
        ["Runtime"]            = [AgentType.BugFix],
        ["Database"]           = [AgentType.BugFix],
    };

    // Heal-cycle agents skip the BugFix→Performance→Review loop themselves
    private static readonly HashSet<AgentType> s_healCycleAgents =
        [AgentType.BugFix, AgentType.Performance, AgentType.Review, AgentType.Supervisor, AgentType.Backlog, AgentType.RequirementsExpander, AgentType.Deploy, AgentType.Build, AgentType.Monitor, AgentType.Planning, AgentType.CodeReasoning, AgentType.DodVerification];

    // Meta-agents track/review items but don't produce deliverables — skip lifecycle Claim/Start/Complete
    private static readonly HashSet<AgentType> s_metaAgents =
        [AgentType.Backlog, AgentType.Supervisor, AgentType.Review, AgentType.RequirementsExpander, AgentType.RequirementAnalyzer, AgentType.CodeReasoning, AgentType.Planning, AgentType.DodVerification];

    // Backlog-driven agents: re-dispatch iteratively as long as InQueue work items exist
    private static readonly HashSet<AgentType> s_backlogDrivenAgents =
        [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration, AgentType.UiUx, AgentType.Testing, AgentType.BugFix];

    // Feedback agents: when these complete with new findings, feed them through
    // RequirementsExpander → Backlog so findings become actionable work items.
    private static readonly HashSet<AgentType> s_feedbackAgents =
        [AgentType.Review, AgentType.Build, AgentType.Deploy, AgentType.Monitor, AgentType.BugFix];

    private const int MaxBuildFixCycles = 3; // Legacy — build-fix now loops until green (stall-detection stops it if no progress)

    // Queue for mid-pipeline requirement injection
    private readonly ConcurrentQueue<List<Requirement>> _pendingRequirements = new();
    // Task completion fan-out tracker: taskId -> set(agentName)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _taskCompletedBy = new(StringComparer.OrdinalIgnoreCase);
    // Shared task tracker bridging the lifecycle policy and multi-agent completion
    private readonly ConcurrentTaskTracker _taskTracker = new();
    // Lifecycle policy enforcing Received → InProgress → Completed/Failed stages for all agents
    private readonly WorkItemLifecyclePolicy _lifecycle;

    public AgentOrchestrator(
        IEnumerable<IAgent> agents,
        ILlmProvider llm,
        IArtifactWriter writer,
        IPipelineEventSink eventSink,
        IAuditLogger audit,
        IHumanGate humanGate,
        IServiceProvider serviceProvider,
        ILogger<AgentOrchestrator> logger)
    {
        _agents = agents;
        _llm = llm;
        _writer = writer;
        _eventSink = eventSink;
        _audit = audit;
        _humanGate = humanGate;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lifecycle = new WorkItemLifecyclePolicy(msg => _logger.LogInformation("{Message}", msg))
        {
            MaxItemRetries = MaxAgentRetries + 1,  // 3 retries per item before backlog
            BatchSize = MaxClaimBatchSize
        };
    }

    public AgentContext? GetCurrentContext() => _current;

    public AgentContext? GetProjectContext(string projectId)
        => _activeContexts.TryGetValue(projectId, out var ctx) ? ctx : null;

    public IReadOnlyDictionary<string, AgentContext> GetActiveContexts()
        => _activeContexts;

    public void ResetContext()
    {
        _current = null;
        _taskCompletedBy.Clear();
        _taskTracker.Clear();
        // Drain mid-pipeline requirement injection queue
        while (_pendingRequirements.TryDequeue(out _)) { }
    }

    // ─── Project-Scoped Pipeline (Phase 9) ─────────────────────────
    /// <summary>
    /// Runs a project-scoped pipeline: resolves the project's tech stack, workflow,
    /// and agent assignments from DB, then delegates to the main daemon loop with
    /// an isolated <see cref="AgentContext"/>.
    /// </summary>
    public async Task<AgentContext> RunProjectPipelineAsync(string projectId, PipelineConfig config, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting project-scoped pipeline for project {ProjectId}", projectId);

        // Create isolated context for this project
        var context = new AgentContext
        {
            RequirementsBasePath = config.RequirementsPath,
            OutputBasePath = config.OutputPath,
            PipelineConfig = config,
            ProjectId = projectId
        };

        // Register in active contexts (supports concurrent pipelines)
        _activeContexts[projectId] = context;

        try
        {
            // Resolve scoped services (workflow engine + agent resolver need DB access)
            using var scope = _serviceProvider.CreateScope();
            var workflowEngine = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionEngine>();
            var agentResolver = scope.ServiceProvider.GetRequiredService<IAgentResolver>();

            // Load workflow from DB (or default)
            await workflowEngine.LoadWorkflowAsync(context, context.WorkflowId, ct);

            // Resolve agents for this project
            var projectAgents = await agentResolver.ResolveAllForProjectAsync(context, ct);
            _logger.LogInformation("Resolved {Count} agents for project {ProjectId}", projectAgents.Count, projectId);

            // ── Load project tech stack from DB ──
            await LoadProjectTechStackAsync(scope, context, projectId, ct);

            // Load historical learnings (project + domain + global) for context enrichment
            await LoadHistoricalLearningsAsync(context, ct);

            // If DB-backed workflow stages were loaded, use stage-driven execution
            if (context.ResolvedStages.Count > 0)
            {
                return await RunStageDrivenPipelineAsync(context, config, projectAgents, workflowEngine, ct);
            }

            // Otherwise fall back to the standard daemon loop
            // Set _current so the existing RunPipelineAsync plumbing works
            _current = context;
            _taskCompletedBy.Clear();
            return await RunDaemonLoopCoreAsync(context, config, ct);
        }
        finally
        {
            _activeContexts.TryRemove(projectId, out _);
        }
    }

    /// <summary>
    /// Executes a project pipeline driven by DB-backed workflow stages.
    /// Stages are processed in order; each stage dispatches its agents in parallel.
    /// Approval gates pause execution until approved.
    /// </summary>
    private async Task<AgentContext> RunStageDrivenPipelineAsync(
        AgentContext context,
        PipelineConfig config,
        IReadOnlyList<IAgent> projectAgents,
        IWorkflowExecutionEngine workflowEngine,
        CancellationToken ct)
    {
        _current = context;
        _taskCompletedBy.Clear();

        _lifecycle.MaxQueueItems = config.MaxQueueItems > 0 ? config.MaxQueueItems : 10;
        _lifecycle.MaxInDevItems = config.MaxInDevItems > 0 ? config.MaxInDevItems : 10;

        if (!string.IsNullOrWhiteSpace(config.OrchestratorInstructions))
            context.OrchestratorInstructions.Add(config.OrchestratorInstructions);

        foreach (var agentType in Enum.GetValues<AgentType>())
            context.AgentStatuses[agentType] = AgentStatus.Idle;

        context.ReportProgress = async (agentType, msg) =>
            await PublishEvent(context, agentType, AgentStatus.Running, msg, 0, ct: ct);

        // Wire incremental work-item persistence callback from config (set by PipelineController)
        if (config.PersistWorkItems is not null)
            context.PersistWorkItems = (runId, items) => config.PersistWorkItems(runId, context.ProjectId, items);

        _logger.LogInformation("Pipeline {RunId} starting (stage-driven mode, {StageCount} stages)",
            context.RunId, context.ResolvedStages.Count);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
            $"Pipeline starting — stage-driven mode, {context.ResolvedStages.Count} stages", ct: ct);

        await _audit.LogAsync(AgentType.Orchestrator, context.RunId, AuditAction.PipelineStarted,
            $"Project pipeline {context.RunId} starting — project {context.ProjectId}, {context.ResolvedStages.Count} stages",
            $"RequirementsPath: {config.RequirementsPath}, OutputPath: {config.OutputPath}",
            severity: AuditSeverity.Info, ct: ct);

        var completedAgents = new HashSet<AgentType>();

        // ── Resume support: pre-populate completed agents from a prior interrupted run ──
        var resumeAgents = config.ResumeCompletedAgents;
        if (resumeAgents is { Count: > 0 })
        {
            foreach (var name in resumeAgents)
            {
                if (Enum.TryParse<AgentType>(name, ignoreCase: true, out var at))
                    completedAgents.Add(at);
            }
            _logger.LogInformation("[Resume] Resuming pipeline — {Count} agents already completed from prior run: {Agents}",
                completedAgents.Count, string.Join(", ", completedAgents));
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                $"Resuming pipeline — skipping {completedAgents.Count} already-completed agents", ct: ct);

            // ── Restore all outputs from completed agents so downstream agents have data ──
            var restored = new List<string>();

            if (config.ResumeRequirements is { Count: > 0 })
            {
                context.Requirements = config.ResumeRequirements;
                restored.Add($"{config.ResumeRequirements.Count} requirements");
            }

            if (config.ResumeExpandedRequirements is { Count: > 0 })
            {
                foreach (var er in config.ResumeExpandedRequirements)
                    context.ExpandedRequirements.Add(er);
                restored.Add($"{config.ResumeExpandedRequirements.Count} backlog items");
            }

            if (config.ResumeDerivedServices is { Count: > 0 })
            {
                context.DerivedServices = config.ResumeDerivedServices;
                restored.Add($"{config.ResumeDerivedServices.Count} derived services");
            }

            if (restored.Count > 0)
            {
                _logger.LogInformation("[Resume] Restored into context: {Details}", string.Join(", ", restored));
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Restored from prior run: {string.Join(", ", restored)}", ct: ct);
            }
        }

        foreach (var stage in context.ResolvedStages.OrderBy(s => s.Order))
        {
            if (ct.IsCancellationRequested) break;

            _logger.LogInformation("[Stage {Order}] {Name} — {Count} agents",
                stage.Order, stage.Name, stage.AgentsInvolved.Count);

            // Resume: skip entire stage if all its agents already completed
            if (stage.AgentsInvolved.All(a => completedAgents.Contains(a)))
            {
                _logger.LogInformation("[Stage {Order}] Skipping entire stage '{Name}' — all agents completed in prior run",
                    stage.Order, stage.Name);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Skipping stage: {stage.Name} (completed in prior run)", ct: ct);
                continue;
            }

            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                $"Entering stage: {stage.Name}", ct: ct);

            // Check approval gate
            if (await workflowEngine.IsApprovalRequiredAsync(context, stage, ct))
            {
                _logger.LogInformation("[Stage {Order}] Approval gate — waiting for human approval", stage.Order);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Stage '{stage.Name}' requires approval — pausing pipeline", ct: ct);

                // Wait for approval via HITL gate
                var decision = await _humanGate.RequestApprovalAsync(new HumanApprovalRequest
                {
                    RunId = context.RunId,
                    RequestingAgent = AgentType.Orchestrator,
                    Title = $"Approval required: {stage.Name}",
                    Description = $"Pipeline stage '{stage.Name}' requires approval to proceed.",
                    Category = HumanGateCategory.ConfigurationChange
                }, ct);

                if (decision != HumanDecision.Approved && decision != HumanDecision.AutoApproved)
                {
                    _logger.LogWarning("[Stage {Order}] Approval denied — stopping pipeline", stage.Order);
                    break;
                }

                await workflowEngine.ApproveGateAsync(context, stage.StageId, "human", ct);
            }

            // Dispatch agents for this stage sequentially in declared order.
            // Agents within a stage have implicit ordering dependencies
            // (e.g. RequirementsReader must complete before RequirementsExpander).
            var domainExcluded = GetDomainExcludedAgents(config);
            foreach (var agentType in stage.AgentsInvolved)
            {
                if (ct.IsCancellationRequested) break;

                if (domainExcluded.Contains(agentType))
                {
                    _logger.LogInformation("[Stage {Order}] Skipping {AgentType} — not applicable for domain '{Domain}'",
                        stage.Order, agentType, config.ProjectDomain);
                    completedAgents.Add(agentType);
                    continue;
                }

                // Resume: skip agents that already completed in a prior run
                if (completedAgents.Contains(agentType))
                {
                    _logger.LogInformation("[Stage {Order}] Skipping {AgentType} — already completed in prior run",
                        stage.Order, agentType);
                    await PublishEvent(context, agentType, AgentStatus.Completed,
                        "Skipped (completed in prior run)", ct: ct);
                    continue;
                }

                // Guard: skip service-dependent agents if no services have been derived
                if (context.DerivedServices.Count == 0 && ServiceCatalogResolver.GetServices(context).Count == 0
                    && s_serviceDependentAgents.Contains(agentType))
                {
                    _logger.LogWarning("[Stage {Order}] Skipping {AgentType} — no DerivedServices available (Architect hasn't succeeded yet)",
                        stage.Order, agentType);
                    await PublishEvent(context, agentType, AgentStatus.Completed,
                        "Skipped — no services derived (Architect must succeed first)", ct: ct);
                    completedAgents.Add(agentType);
                    continue;
                }

                var agent = projectAgents.FirstOrDefault(a => a.Type == agentType)
                            ?? _agents.FirstOrDefault(a => a.Type == agentType);
                if (agent is null)
                {
                    _logger.LogWarning("[Stage {Order}] Agent {AgentType} not found — skipping", stage.Order, agentType);
                    completedAgents.Add(agentType);
                    continue;
                }

                _logger.LogInformation("[Stage {Order}] Running {AgentType}...", stage.Order, agentType);
                var success = await RunAgentWithHealingAsync(context, agent, ct);
                completedAgents.Add(agentType);

                if (!success)
                {
                    _logger.LogWarning("[Stage {Order}] Agent {AgentType} failed — continuing with next agent", stage.Order, agentType);
                }
            }

            // Check exit criteria
            var readonlyCompleted = (IReadOnlySet<AgentType>)completedAgents;
            if (!workflowEngine.IsStageComplete(context, stage, readonlyCompleted))
            {
                _logger.LogWarning("[Stage {Order}] Exit criteria not met for stage '{Name}'", stage.Order, stage.Name);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Stage '{stage.Name}' exit criteria not fully met — continuing", ct: ct);
            }
        }

        // Write artifacts
        if (context.Artifacts.Count > 0 && !string.IsNullOrEmpty(config.OutputPath))
        {
            await _writer.WriteAllAsync(context.Artifacts, config.OutputPath, ct);
            _logger.LogInformation("Wrote {Count} artifacts to {Path}", context.Artifacts.Count, config.OutputPath);
        }

        context.CompletedAt = DateTimeOffset.UtcNow;
        context.AgentStatuses[AgentType.Orchestrator] = AgentStatus.Completed;

        _logger.LogInformation(
            "Project pipeline {RunId} completed — {Artifacts} artifacts, {Findings} findings",
            context.RunId, context.Artifacts.Count, context.Findings.Count);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
            $"Project pipeline completed — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings",
            artifactCount: context.Artifacts.Count, findingCount: context.Findings.Count, ct: CancellationToken.None);

        // Persist learnings: harvest from this run + verify previously-loaded learnings
        await PersistLearningsAsync(context, CancellationToken.None);

        // Persist communication log if enabled
        await PersistCommunicationLogAsync(context, CancellationToken.None);

        await ExecutePostPipelineInstructions(context, config, CancellationToken.None);

        return context;
    }

    /// <summary>
    /// Core daemon loop extracted for reuse by both <see cref="RunPipelineAsync"/>
    /// and <see cref="RunProjectPipelineAsync"/> (legacy fallback).
    /// Reuses the pre-built context so project-scoped state (ProjectId, workflow data) is preserved.
    /// </summary>
    private Task<AgentContext> RunDaemonLoopCoreAsync(AgentContext context, PipelineConfig config, CancellationToken ct)
    {
        _current = context;
        _taskCompletedBy.Clear();
        return RunDaemonLoopAsync(context, config, ct);
    }

    // ─── Mid-Pipeline Requirement Injection ────────────────────────
    public async Task AddRequirementsAsync(List<Requirement> newRequirements, CancellationToken ct = default)
    {
        if (newRequirements.Count == 0) return;

        var context = _current;
        if (context is null)
        {
            _logger.LogWarning("AddRequirementsAsync called but no active pipeline");
            return;
        }

        // 1. Persist to docs folder on disk
        var docsPath = context.RequirementsBasePath;
        if (!string.IsNullOrEmpty(docsPath) && Directory.Exists(docsPath))
        {
            var fileName = $"user-requirements-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.md";
            var filePath = Path.Combine(docsPath, fileName);

            var md = new System.Text.StringBuilder();
            md.AppendLine($"# User-Submitted Requirements ({DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss UTC})");
            md.AppendLine();
            foreach (var req in newRequirements)
            {
                md.AppendLine($"## {req.Title}");
                md.AppendLine();
                md.AppendLine(req.Description);
                if (req.AcceptanceCriteria.Count > 0)
                {
                    md.AppendLine();
                    md.AppendLine("### Acceptance Criteria");
                    foreach (var ac in req.AcceptanceCriteria)
                        md.AppendLine($"- {ac}");
                }
                if (req.Tags.Count > 0)
                    md.AppendLine($"\n**Tags:** {string.Join(", ", req.Tags)}");
                md.AppendLine();
            }

            await File.WriteAllTextAsync(filePath, md.ToString(), ct);
            _logger.LogInformation("Persisted {Count} new requirements to {Path}", newRequirements.Count, filePath);

            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                $"New requirements saved to {fileName} — {newRequirements.Count} items", ct: ct);
        }

        // 2. Add to context and enqueue for orchestrator to pick up
        foreach (var req in newRequirements)
            context.Requirements.Add(req);

        _pendingRequirements.Enqueue(newRequirements);
        _logger.LogInformation("Enqueued {Count} requirements for pipeline processing", newRequirements.Count);

        // 3. Immediately expand to backlog
        context.DirectiveQueue.Enqueue(new AgentDirective
        {
            From = AgentType.Orchestrator,
            To = AgentType.RequirementsExpander,
            Action = "EXPAND_NEW",
            Details = $"Expand {newRequirements.Count} new user-submitted requirements",
            Priority = 1
        });

        context.DirectiveQueue.Enqueue(new AgentDirective
        {
            From = AgentType.Orchestrator,
            To = AgentType.Backlog,
            Action = "REFRESH_BACKLOG",
            Details = $"Process {newRequirements.Count} new requirements into backlog"
        });

        // 4. Re-run RequirementsExpander and Backlog agents
        var expander = _agents.FirstOrDefault(a => a.Type == AgentType.RequirementsExpander);
        if (expander is not null)
            await RunAgentWithHealingAsync(context, expander, ct);

        var backlog = _agents.FirstOrDefault(a => a.Type == AgentType.Backlog);
        if (backlog is not null)
            await RunAgentWithHealingAsync(context, backlog, ct);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
            $"Requirements expanded and backlog updated — {context.ExpandedRequirements.Count} work items total", ct: ct);
    }

    // ─── Main Daemon Loop ───────────────────────────────────────────
    public async Task<AgentContext> RunPipelineAsync(PipelineConfig config, CancellationToken ct = default)
    {
        var context = new AgentContext
        {
            RequirementsBasePath = config.RequirementsPath,
            OutputBasePath = config.OutputPath,
            PipelineConfig = config,
            ProjectId = config.ProjectId
        };
        _taskCompletedBy.Clear();
        _current = context;

        // Load project tech stack from DB if ProjectId is set, else apply defaults
        if (!string.IsNullOrWhiteSpace(config.ProjectId))
        {
            using var scope = _serviceProvider.CreateScope();
            await LoadProjectTechStackAsync(scope, context, config.ProjectId, ct);
        }

        // Load historical learnings (project + domain + global)
        await LoadHistoricalLearningsAsync(context, ct);

        return await RunDaemonLoopAsync(context, config, ct);
    }

    /// <summary>Shared daemon loop implementation used by both RunPipelineAsync and RunDaemonLoopCoreAsync.</summary>
    private async Task<AgentContext> RunDaemonLoopAsync(AgentContext context, PipelineConfig config, CancellationToken ct)
    {
        // Apply WIP limits from config to lifecycle policy
        _lifecycle.MaxQueueItems = config.MaxQueueItems > 0 ? config.MaxQueueItems : 10;
        _lifecycle.MaxInDevItems = config.MaxInDevItems > 0 ? config.MaxInDevItems : 10;

        if (!string.IsNullOrWhiteSpace(config.OrchestratorInstructions))
            context.OrchestratorInstructions.Add(config.OrchestratorInstructions);

        foreach (var agentType in Enum.GetValues<AgentType>())
            context.AgentStatuses[agentType] = AgentStatus.Idle;

        // Wire progress callback once — agents pass their own AgentType to avoid cross-contamination
        context.ReportProgress = async (agentType, msg) =>
            await PublishEvent(context, agentType, AgentStatus.Running, msg, 0, ct: ct);

        // Wire incremental work-item persistence callback from config (set by PipelineController)
        if (config.PersistWorkItems is not null)
            context.PersistWorkItems = (runId, items) => config.PersistWorkItems(runId, context.ProjectId, items);

        _logger.LogInformation("Pipeline {RunId} starting (daemon parallel mode)", context.RunId);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
            "Pipeline starting — daemon parallel mode", ct: ct);

        // ── Audit: pipeline started
        await _audit.LogAsync(AgentType.Orchestrator, context.RunId, AuditAction.PipelineStarted,
            $"Pipeline {context.RunId} starting — {Enum.GetValues<AgentType>().Length} agents, daemon parallel mode",
            $"RequirementsPath: {config.RequirementsPath}, OutputPath: {config.OutputPath}",
            severity: AuditSeverity.Info, ct: ct);

        try
        {

        // Build the work queue: all agents we want to run
        var pendingAgents = new HashSet<AgentType>(s_dependencies.Keys);
        pendingAgents.Remove(AgentType.Supervisor);  // Supervisor runs at the very end
        // Deploy/Monitor/LoadTest are post-backlog gates and should not be daemon-dispatched early.
        // Build is dispatched via normal dependency flow so it runs as soon as code-gen + review + bugfix complete.
        pendingAgents.Remove(AgentType.Deploy);
        pendingAgents.Remove(AgentType.Monitor);
        pendingAgents.Remove(AgentType.LoadTest);

        // Domain-specific agent exclusion: skip agents that only apply to certain domains
        foreach (var excluded in GetDomainExcludedAgents(config))
            pendingAgents.Remove(excluded);
        var completedAgents = new ConcurrentDictionary<AgentType, bool>(); // true=success, false=skipped
        var runningAgents = new ConcurrentDictionary<AgentType, Task>();
        var platformExpandTriggered = false;

        // ── Resume support: pre-populate completed agents and context from a prior interrupted run ──
        var resumeAgents = config.ResumeCompletedAgents;
        if (resumeAgents is { Count: > 0 })
        {
            foreach (var name in resumeAgents)
            {
                if (Enum.TryParse<AgentType>(name, ignoreCase: true, out var at))
                {
                    completedAgents[at] = true;
                    pendingAgents.Remove(at);
                    context.AgentStatuses[at] = AgentStatus.Completed;
                }
            }
            _logger.LogInformation("[Resume] Resuming daemon pipeline — {Count} agents already completed from prior run: {Agents}",
                completedAgents.Count, string.Join(", ", completedAgents.Keys));

            var restored = new List<string>();

            if (config.ResumeRequirements is { Count: > 0 })
            {
                context.Requirements = config.ResumeRequirements;
                restored.Add($"{config.ResumeRequirements.Count} requirements");
            }

            if (config.ResumeExpandedRequirements is { Count: > 0 })
            {
                foreach (var er in config.ResumeExpandedRequirements)
                    context.ExpandedRequirements.Add(er);
                restored.Add($"{config.ResumeExpandedRequirements.Count} backlog items");
            }

            if (config.ResumeDerivedServices is { Count: > 0 })
            {
                context.DerivedServices = config.ResumeDerivedServices;
                restored.Add($"{config.ResumeDerivedServices.Count} derived services");
            }

            if (restored.Count > 0)
            {
                _logger.LogInformation("[Resume] Restored into daemon context: {Details}", string.Join(", ", restored));
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Restored from prior run: {string.Join(", ", restored)}", ct: ct);
            }
        }
        // Track finding count before each feedback agent runs so we detect new findings
        var findingCountBeforeAgent = new ConcurrentDictionary<AgentType, int>();
        // Track how many times Review has triggered remediation dispatch (allows iterative cycles)
        var lastRemediationFindingCount = 0;

        // Track stalled-tick count so the loop backs off to idle polling when no progress is made
        var stalledTicks = 0;
        var lastCompletedCount = 0;
        const int MaxStalledTicks = 10;
        // Track whether Build/Deploy/Monitor/Supervisor have run for the current backlog wave
        var buildDeployRanForWave = false;
        var lastArtifactWriteCount = 0;
        // Track whether BacklogAgent has run at least once — code-gen agents wait for this
        // before their first dispatch so they have InQueue items to claim.
        var backlogRanAtLeastOnce = false;
        // Cooldown: minimum interval between BacklogAgent re-queues to avoid spinning
        var lastBacklogCompletedAt = DateTimeOffset.MinValue;
        var backlogCooldown = TimeSpan.FromSeconds(10);

        // ── Daemon loop: runs 24/7 until cancellation ──
        // Accepts new requirements from users/monitoring agents at any time via _pendingRequirements.
        while (!ct.IsCancellationRequested)
        {

            // Process any mid-pipeline requirement injections
            await ProcessPendingRequirements(context, pendingAgents, completedAgents, ct);

            // Process inter-agent directives
            ProcessDirectives(context, pendingAgents, completedAgents);

            // Re-evaluate blocked work items — unblock items whose deps are now Completed
            var unblocked = ReevaluateBlockedItems(context);
            if (unblocked > 0)
                _logger.LogInformation("[Daemon] Unblocked {Count} work item(s) whose dependencies are now complete", unblocked);

            // Sweep stuck InProgress items: if all relevant agents have completed their runs
            // AND none are currently running, force-complete items that the task tracker missed.
            SweepStuckInProgressItems(context, completedAgents, runningAgents);

            ApplyAdaptiveWipLimits(context);

            // Find agents whose dependencies are all satisfied
            var readyBatch = pendingAgents
                .Where(a => GetDependencies(a).All(dep => completedAgents.ContainsKey(dep)))
                // Code-gen agents must wait until Backlog has run at least once
                // so there are InQueue items to claim.
                .Where(a => !s_backlogDrivenAgents.Contains(a) || backlogRanAtLeastOnce)
                .ToList();

            // Dispatch all ready agents in parallel
            foreach (var agentType in readyBatch)
            {
                pendingAgents.Remove(agentType);
                var agent = _agents.FirstOrDefault(a => a.Type == agentType);
                if (agent is null)
                {
                    completedAgents[agentType] = false;
                    // If the Backlog agent is not registered, unblock backlog-driven agents
                    if (agentType == AgentType.Backlog)
                        backlogRanAtLeastOnce = true;
                    continue;
                }

                _logger.LogInformation("[Daemon] Dispatching {Agent} (deps satisfied)", agentType);

                // Report dispatch + backlog assignment via Orchestrator progress
                var claimed = GetClaimableItems(context, agentType);
                var dispatchMsg = claimed.Count > 0
                    ? $"Dispatching {agent.Name} — assigned {claimed.Count} backlog items: {string.Join(", ", claimed.Take(3).Select(i => Truncate(i.Title, 50)))}{(claimed.Count > 3 ? $" (+{claimed.Count - 3} more)" : "")}"
                    : $"Dispatching {agent.Name} — dependencies satisfied";
                _ = PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running, dispatchMsg, 0, ct: ct);

                var task = Task.Run(async () =>
                {
                    // Snapshot finding count before execution so we can detect new findings
                    if (s_feedbackAgents.Contains(agentType))
                        findingCountBeforeAgent[agentType] = context.Findings.Count;

                    bool success;
                    // Build agent uses the build→fix→rebuild cycle instead of a single run
                    if (agentType == AgentType.Build)
                    {
                        success = await RunBuildFixCycleAsync(context, agent, ct);
                    }
                    else
                    {
                        success = await RunAgentWithHealingAsync(context, agent, ct);
                    }
                    completedAgents[agentType] = success;
                    runningAgents.TryRemove(agentType, out _);

                    // Once BacklogAgent has run at least once, code-gen agents may dispatch
                    if (agentType == AgentType.Backlog)
                    {
                        backlogRanAtLeastOnce = true;
                        lastBacklogCompletedAt = DateTimeOffset.UtcNow;
                    }
                }, ct);
                runningAgents[agentType] = task;
            }

            // After PlatformBuilder completes — feed output through RequirementsExpander → Backlog
            // so platform-derived technical requirements become backlog work items.
            if (!platformExpandTriggered && completedAgents.ContainsKey(AgentType.PlatformBuilder))
            {
                platformExpandTriggered = true;
                if (completedAgents.ContainsKey(AgentType.RequirementsExpander)
                    && !pendingAgents.Contains(AgentType.RequirementsExpander)
                    && !runningAgents.ContainsKey(AgentType.RequirementsExpander))
                {
                    completedAgents.TryRemove(AgentType.RequirementsExpander, out _);
                    pendingAgents.Add(AgentType.RequirementsExpander);
                    _logger.LogInformation("[Daemon] PlatformBuilder done — re-queuing RequirementsExpander for platform-derived requirements");
                }
                if (completedAgents.ContainsKey(AgentType.Backlog)
                    && !pendingAgents.Contains(AgentType.Backlog)
                    && !runningAgents.ContainsKey(AgentType.Backlog))
                {
                    completedAgents.TryRemove(AgentType.Backlog, out _);
                    pendingAgents.Add(AgentType.Backlog);
                    _logger.LogInformation("[Daemon] Re-queuing Backlog to process platform-derived expanded requirements");
                }
            }

            // Backlog-driven re-dispatch: iteratively re-queue code-gen agents
            // while InQueue work items remain for them to claim.
            // NOTE: We only gate on PlatformBuilder (not Backlog) because Backlog
            // is continuously cycling to promote items. Requiring Backlog in
            // completedAgents would starve code-gen agents during Backlog runs.
            if (completedAgents.ContainsKey(AgentType.PlatformBuilder))
            {
                foreach (var codeGenType in s_backlogDrivenAgents)
                {
                    if (pendingAgents.Contains(codeGenType) || runningAgents.ContainsKey(codeGenType))
                        continue;
                    if (!completedAgents.ContainsKey(codeGenType))
                        continue; // hasn't run yet — normal dep-based dispatch will handle it

                    var claimable = GetClaimableItems(context, codeGenType);
                    if (claimable.Count > 0)
                    {
                        _logger.LogInformation("[Daemon] Backlog-driven re-dispatch: {Agent} — {Count} claimable item(s)", codeGenType, claimable.Count);
                        completedAgents.TryRemove(codeGenType, out _);
                        pendingAgents.Add(codeGenType);
                    }
                }
            }

            // Re-queue BacklogAgent when New items need promotion (independent of code-gen block)
            // Cooldown prevents tight-loop re-queuing when WIP limits block promotion.
            {
                var backlogCooldownElapsed = DateTimeOffset.UtcNow - lastBacklogCompletedAt >= backlogCooldown;
                var newItemCount = context.ExpandedRequirements.Count(i =>
                    IsActionableWorkItem(i) && i.Status == WorkItemStatus.New);
                if (newItemCount > 0
                    && backlogCooldownElapsed
                    && completedAgents.ContainsKey(AgentType.Backlog)
                    && !pendingAgents.Contains(AgentType.Backlog)
                    && !runningAgents.ContainsKey(AgentType.Backlog))
                {
                    _logger.LogInformation("[Daemon] Re-queuing BacklogAgent — {Count} New items still need promotion", newItemCount);
                    completedAgents.TryRemove(AgentType.Backlog, out _);
                    pendingAgents.Add(AgentType.Backlog);
                }
            }

            // Feedback-driven expansion: when a feedback agent (Review, Build, Deploy,
            // Monitor, BugFix) completes with new findings, re-queue RequirementsExpander
            // → Backlog so findings are expanded into actionable work items.
            // Also re-queue the feedback agents themselves for iterative cycles.
            foreach (var fbAgent in s_feedbackAgents)
            {
                if (!completedAgents.ContainsKey(fbAgent)) continue;
                if (!findingCountBeforeAgent.TryRemove(fbAgent, out var prevCount)) continue;

                var newFindings = context.Findings.Count - prevCount;
                if (newFindings <= 0) continue;

                _logger.LogInformation(
                    "[Daemon] Feedback agent {Agent} produced {Count} new finding(s) — triggering RequirementsExpander → Backlog",
                    fbAgent, newFindings);

                // Re-queue RequirementsExpander and Backlog to process findings into work items
                if (!pendingAgents.Contains(AgentType.RequirementsExpander)
                    && !runningAgents.ContainsKey(AgentType.RequirementsExpander))
                {
                    completedAgents.TryRemove(AgentType.RequirementsExpander, out _);
                    pendingAgents.Add(AgentType.RequirementsExpander);
                }
                if (!pendingAgents.Contains(AgentType.Backlog)
                    && !runningAgents.ContainsKey(AgentType.Backlog))
                {
                    completedAgents.TryRemove(AgentType.Backlog, out _);
                    pendingAgents.Add(AgentType.Backlog);
                }

                // Re-queue Review after non-Review feedback agents produce findings,
                // so the new/fixed code gets reviewed again.
                if (fbAgent != AgentType.Review
                    && !pendingAgents.Contains(AgentType.Review)
                    && !runningAgents.ContainsKey(AgentType.Review))
                {
                    completedAgents.TryRemove(AgentType.Review, out _);
                    pendingAgents.Add(AgentType.Review);
                    _logger.LogInformation("[Daemon] Re-queuing Review — {Agent} produced new findings", fbAgent);
                }
                break; // one trigger per loop tick is enough
            }

            // After Review completes — dispatch remediation agents for any new findings.
            // Uses a generation counter instead of a one-shot flag so Review → BugFix →
            // Review cycles can repeat as long as new findings keep appearing.
            if (completedAgents.ContainsKey(AgentType.Review) && context.Findings.Count > lastRemediationFindingCount)
            {
                var newSinceLastRemediation = context.Findings.Count - lastRemediationFindingCount;
                lastRemediationFindingCount = context.Findings.Count;

                _logger.LogInformation("[Daemon] Review cycle — {Count} new finding(s) since last remediation pass", newSinceLastRemediation);

                var remediationTypes = new HashSet<AgentType>();
                // Only scan the new findings (tail of the list)
                foreach (var finding in context.Findings.Skip(context.Findings.Count - newSinceLastRemediation).ToList())
                {
                    if (s_findingDispatch.TryGetValue(finding.Category, out var agents))
                        foreach (var at in agents)
                            remediationTypes.Add(at);
                }

                foreach (var rt in remediationTypes)
                {
                    if (!pendingAgents.Contains(rt) && !runningAgents.ContainsKey(rt))
                    {
                        completedAgents.TryRemove(rt, out _);
                        _logger.LogInformation("[Daemon] Remediation dispatch: {Agent}", rt);
                        pendingAgents.Add(rt);
                    }
                }

                // Re-run Backlog to update statuses
                if (!pendingAgents.Contains(AgentType.Backlog) && !runningAgents.ContainsKey(AgentType.Backlog))
                {
                    completedAgents.TryRemove(AgentType.Backlog, out _);
                    pendingAgents.Add(AgentType.Backlog);
                    _logger.LogInformation("[Daemon] Re-running Backlog after Review to update statuses");
                }
            }

            // Wait a tick before next poll (or until a running task completes)
            if (runningAgents.Count > 0)
            {
                try
                {
                    await Task.WhenAny(
                        Task.WhenAny(runningAgents.Values),
                        Task.Delay(DaemonPollInterval, ct));
                }
                catch (OperationCanceledException) { break; }
            }
            else if (pendingAgents.Count > 0)
            {
                // No running tasks but pending agents exist — they might have unmet deps
                var blocked = pendingAgents
                    .Where(a => GetDependencies(a).Any(dep => !completedAgents.ContainsKey(dep) && !pendingAgents.Contains(dep) && !runningAgents.ContainsKey(dep)))
                    .ToList();

                if (blocked.Count > 0 && blocked.Count == pendingAgents.Count)
                {
                    _logger.LogWarning("[Daemon] {Count} agents have unmet deps — dispatching anyway", blocked.Count);
                    foreach (var bt in blocked)
                    {
                        foreach (var dep in GetDependencies(bt))
                            completedAgents.TryAdd(dep, false);
                    }
                }
                else
                {
                    try { await Task.Delay(DaemonPollInterval, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
            else
            {
                // No pending or running agents — only re-queue Backlog when actionable items remain
                // and cooldown has elapsed.
                var hasActionable = context.ExpandedRequirements.Any(i =>
                    IsActionableWorkItem(i) && i.Status is not WorkItemStatus.Completed);
                var cooldownOk = DateTimeOffset.UtcNow - lastBacklogCompletedAt >= backlogCooldown;
                if (hasActionable && cooldownOk && !pendingAgents.Contains(AgentType.Backlog) && !runningAgents.ContainsKey(AgentType.Backlog))
                {
                    completedAgents.TryRemove(AgentType.Backlog, out _);
                    pendingAgents.Add(AgentType.Backlog);
                    _logger.LogInformation("[Daemon] Re-queuing Backlog — actionable items still incomplete");
                }

                // Detect stalled state: back off to longer idle poll when no progress
                var currentCompleted = context.ExpandedRequirements.Count(i => IsActionableWorkItem(i) && i.Status == WorkItemStatus.Completed);
                var currentInProgress = context.ExpandedRequirements.Count(i => IsActionableWorkItem(i) && i.Status == WorkItemStatus.InProgress);
                var currentInQueue = context.ExpandedRequirements.Count(i => IsActionableWorkItem(i) && i.Status == WorkItemStatus.InQueue);
                var progressSnapshot = currentCompleted + currentInProgress * 1000 + currentInQueue * 1000000;
                if (progressSnapshot == lastCompletedCount)
                {
                    stalledTicks++;
                    if (stalledTicks >= MaxStalledTicks)
                    {
                        _logger.LogInformation("[Daemon] Idle — no progress for {Ticks} ticks. {Done}/{Total} actionable items completed. Waiting for new input...",
                            stalledTicks, currentCompleted, context.ExpandedRequirements.Count(IsActionableWorkItem));
                        // Back off to 5-second idle poll instead of 500ms to reduce CPU usage
                        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                        catch (OperationCanceledException) { break; }
                        continue;
                    }
                }
                else
                {
                    stalledTicks = 0;
                    lastCompletedCount = progressSnapshot;
                }

                try { await Task.Delay(DaemonPollInterval, ct); }
                catch (OperationCanceledException) { break; }
            }

            // ── Incremental artifact flush: write new artifacts to disk as soon as agents produce them ──
            if (context.Artifacts.Count > lastArtifactWriteCount && !string.IsNullOrEmpty(config.OutputPath))
            {
                var newArtifacts = context.Artifacts.Skip(lastArtifactWriteCount).ToList();
                foreach (var artifact in newArtifacts)
                    await _writer.WriteAsync(artifact, config.OutputPath, ct);
                _logger.LogInformation("[Daemon] Incremental flush: wrote {New} new artifact(s) to {Path} (total: {Total})",
                    newArtifacts.Count, config.OutputPath, context.Artifacts.Count);
                lastArtifactWriteCount = context.Artifacts.Count;
            }

            // ── Inside-loop: when all actionable backlog items complete, run Build/Deploy/Monitor/Supervisor ──
            var allWorkDone = IsActionableBacklogCompleted(context)
                || !context.ExpandedRequirements.Any(IsActionableWorkItem); // empty backlog = nothing to wait for
            if (allWorkDone && !buildDeployRanForWave
                && pendingAgents.Count == 0 && runningAgents.Count == 0)
            {
                buildDeployRanForWave = true;

                // Background Review pass: re-validate all artifacts
                var reviewAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Review);
                if (reviewAgent is not null)
                {
                    _logger.LogInformation("[Daemon] Running background Review pass on all artifacts");
                    context.ReviewIteration++;
                    await RunAgentWithHealingAsync(context, reviewAgent, ct);

                    var backlogAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Backlog);
                    if (backlogAgent is not null)
                        await RunAgentWithHealingAsync(context, backlogAgent, ct);
                }

                // Build → Fix → Rebuild (skip if Build already ran successfully via normal dispatch)
                var buildAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Build);
                var buildPassed = completedAgents.TryGetValue(AgentType.Build, out var buildWasSuccess) && buildWasSuccess;
                if (buildAgent is not null && !buildPassed)
                    buildPassed = await RunBuildFixCycleAsync(context, buildAgent, ct);

                // Deploy → Monitor (only if build passed)
                var deployAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Deploy);
                if (deployAgent is not null && buildPassed)
                {
                    await RunAgentWithHealingAsync(context, deployAgent, ct);
                    var monitorAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Monitor);
                    if (monitorAgent is not null)
                        await RunMonitorFixCycleAsync(context, monitorAgent, deployAgent, ct);
                }
                else if (deployAgent is not null && !buildPassed)
                {
                    _logger.LogWarning("[Daemon] Skipping deployment — build-fix cycle stalled");
                    await PublishEvent(context, AgentType.Deploy, AgentStatus.Failed,
                        "Deployment SKIPPED — build has unresolved errors.", ct: ct);
                }

                // Supervisor report
                var supervisor = _agents.FirstOrDefault(a => a.Type == AgentType.Supervisor);
                if (supervisor is not null)
                {
                    _logger.LogInformation("[Daemon] Running Supervisor report");
                    await RunAgentWithHealingAsync(context, supervisor, ct);
                }

                // Write artifacts to disk
                if (context.Artifacts.Count > lastArtifactWriteCount && !string.IsNullOrEmpty(config.OutputPath))
                {
                    await _writer.WriteAllAsync(context.Artifacts, config.OutputPath, ct);
                    lastArtifactWriteCount = context.Artifacts.Count;
                    _logger.LogInformation("Wrote {Count} artifacts to {Path}", context.Artifacts.Count, config.OutputPath);
                }

                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Wave complete — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings. Awaiting new input...",
                    artifactCount: context.Artifacts.Count, findingCount: context.Findings.Count, ct: ct);

                _logger.LogInformation("[Daemon] Wave complete. Orchestrator continues to accept new requirements.");
            }

            // When new requirements arrive, reset the build/deploy gate for the next wave
            if (!IsActionableBacklogCompleted(context) && buildDeployRanForWave)
            {
                buildDeployRanForWave = false;
                _logger.LogInformation("[Daemon] New work detected — starting new wave");
            }
        }

        // ── Graceful shutdown: loop exited via cancellation ──────────
        _logger.LogInformation("[Daemon] Orchestrator shutting down (cancellation requested)");

        // Final artifact flush
        if (context.Artifacts.Count > 0 && !string.IsNullOrEmpty(config.OutputPath))
        {
            await _writer.WriteAllAsync(context.Artifacts, config.OutputPath, CancellationToken.None);
            _logger.LogInformation("Wrote {Count} artifacts to {Path}", context.Artifacts.Count, config.OutputPath);
        }

        context.CompletedAt = DateTimeOffset.UtcNow;
        context.AgentStatuses[AgentType.Orchestrator] = AgentStatus.Completed;
        _logger.LogInformation(
            "Pipeline {RunId} stopped — {Artifacts} artifacts, {Findings} findings, {Tests} diagnostics, {Backlog} backlog items",
            context.RunId, context.Artifacts.Count, context.Findings.Count, context.TestDiagnostics.Count, context.ExpandedRequirements.Count);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
            $"Pipeline stopped — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings, {context.ExpandedRequirements.Count} backlog items",
            artifactCount: context.Artifacts.Count, findingCount: context.Findings.Count, ct: CancellationToken.None);

        var pipelineDuration = context.CompletedAt.HasValue && context.StartedAt != default
            ? $"{(context.CompletedAt.Value - context.StartedAt).TotalSeconds:F0}s"
            : "unknown";
        await _audit.LogAsync(AgentType.Orchestrator, context.RunId, AuditAction.PipelineCompleted,
            $"Pipeline {context.RunId} stopped — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings",
            $"Backlog: {context.ExpandedRequirements.Count}, TestDiagnostics: {context.TestDiagnostics.Count}, " +
            $"Duration: {pipelineDuration}",
            severity: AuditSeverity.Info, ct: CancellationToken.None);

        // Persist learnings: harvest from this run + verify previously-loaded learnings
        await PersistLearningsAsync(context, CancellationToken.None);

        // Persist communication log if enabled
        await PersistCommunicationLogAsync(context, CancellationToken.None);

        await ExecutePostPipelineInstructions(context, config, CancellationToken.None);

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Pipeline {RunId} crashed — {ExType}: {Message}", context.RunId, ex.GetType().Name, ex.Message);
            context.CompletedAt ??= DateTimeOffset.UtcNow;
            context.AgentStatuses[AgentType.Orchestrator] = AgentStatus.Failed;

            // Reset any agents stuck in Running state so they don't appear hung
            foreach (var kv in context.AgentStatuses)
            {
                if (kv.Value == AgentStatus.Running && kv.Key != AgentType.Orchestrator)
                    context.AgentStatuses[kv.Key] = AgentStatus.Failed;
            }

            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Failed,
                $"Pipeline crashed: {ex.GetType().Name}: {ex.Message}", ct: ct);
            throw;
        }

        return context;
    }

    // ─── Process mid-pipeline requirements ──────────────────────────
    private async Task ProcessPendingRequirements(
        AgentContext context,
        HashSet<AgentType> pendingAgents,
        ConcurrentDictionary<AgentType, bool> completedAgents,
        CancellationToken ct)
    {
        while (_pendingRequirements.TryDequeue(out var newReqs))
        {
            _logger.LogInformation("[Daemon] Processing {Count} injected requirements", newReqs.Count);

            // Re-queue the core build agents to process new requirements
            var agentsToRerun = new[]
            {
                AgentType.RequirementsExpander,
                AgentType.Backlog,
                AgentType.Architect,
                AgentType.PlatformBuilder,
                AgentType.Database,
                AgentType.ServiceLayer,
                AgentType.Application,
                AgentType.Integration,
                AgentType.Testing
            };
            foreach (var at in agentsToRerun)
            {
                if (!pendingAgents.Contains(at))
                {
                    completedAgents.TryRemove(at, out _);
                    pendingAgents.Add(at);
                    context.AgentStatuses[at] = AgentStatus.Idle;

                    await PublishEvent(context, at, AgentStatus.Idle,
                        $"Re-queued for {newReqs.Count} new requirements", ct: ct);
                }
            }

            // Queue a new Review pass after build agents finish
            if (!pendingAgents.Contains(AgentType.Review))
            {
                completedAgents.TryRemove(AgentType.Review, out _);
                pendingAgents.Add(AgentType.Review);
            }

            context.DevIteration++;
        }
    }

    // ─── Process inter-agent directives ─────────────────────────────
    private void ProcessDirectives(
        AgentContext context,
        HashSet<AgentType> pendingAgents,
        ConcurrentDictionary<AgentType, bool> completedAgents)
    {
        var snapshot = new List<AgentDirective>();
        while (context.DirectiveQueue.TryDequeue(out var d))
            snapshot.Add(d);

        foreach (var directive in snapshot)
        {
            _logger.LogInformation("[Directive] {From} → {To}: {Action}", directive.From, directive.To, directive.Action);

            switch (directive.Action)
            {
                case "RE_RUN" when s_dependencies.ContainsKey(directive.To):
                    if (!pendingAgents.Contains(directive.To))
                    {
                        completedAgents.TryRemove(directive.To, out _);
                        pendingAgents.Add(directive.To);
                    }
                    break;

                case "EXPAND_NEW":
                case "EXPAND_GAPS":
                case "REFRESH_BACKLOG":
                    // Schedule the target agent for a fresh run; do not re-enqueue
                    // otherwise the same directives loop forever in the daemon.
                    if (s_dependencies.ContainsKey(directive.To) && !pendingAgents.Contains(directive.To))
                    {
                        completedAgents.TryRemove(directive.To, out _);
                        pendingAgents.Add(directive.To);
                    }
                    break;
            }
        }
    }

    public async Task<AgentContext> RunSingleAgentAsync(PipelineConfig config, AgentType agentType, CancellationToken ct = default)
    {
        var context = _current ?? new AgentContext
        {
            RequirementsBasePath = config.RequirementsPath,
            OutputBasePath = config.OutputPath,
            ProjectId = config.ProjectId
        };
        _current = context;

        var agent = _agents.FirstOrDefault(a => a.Type == agentType)
            ?? throw new InvalidOperationException($"Agent {agentType} not registered.");

        await PrimeAgentWithLlmAsync(context, agent, ct);
        await agent.ExecuteAsync(context, ct);
        context.StampProjectScope();
        return context;
    }

    // ─── Rerun Failed Agents ────────────────────────────────────────
    // After the main daemon loop, re-attempt agents that failed.
    // This handles transient failures (DB connectivity, Docker not ready, etc.)
    // by giving them one more shot before Review/Build/Deploy.

    private static readonly HashSet<AgentType> s_serviceDependentAgents =
    [
        AgentType.Database, AgentType.ServiceLayer, AgentType.Application,
        AgentType.Integration, AgentType.Migration
    ];

    private static readonly HashSet<AgentType> s_rerunCandidates =
    [
        AgentType.Architect, AgentType.Database, AgentType.ServiceLayer, AgentType.Application,
        AgentType.Integration, AgentType.Testing
    ];

    private async Task RerunFailedAgentsAsync(
        AgentContext context,
        ConcurrentDictionary<AgentType, bool> completedAgents,
        CancellationToken ct)
    {
        var failedAgents = context.AgentStatuses
            .Where(kv => kv.Value == AgentStatus.Failed && s_rerunCandidates.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

        if (failedAgents.Count == 0) return;

        _logger.LogInformation("[Daemon] Retrying {Count} failed agent(s): {Agents}",
            failedAgents.Count, string.Join(", ", failedAgents));
        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
            $"Retrying {failedAgents.Count} failed agent(s): {string.Join(", ", failedAgents)}", ct: ct);

        foreach (var agentType in failedAgents)
        {
            ct.ThrowIfCancellationRequested();

            var agent = _agents.FirstOrDefault(a => a.Type == agentType);
            if (agent is null) continue;

            _logger.LogInformation("[Daemon] Re-running failed agent: {Agent}", agentType);
            await PublishEvent(context, agentType, AgentStatus.Running,
                $"Re-running {agent.Name} (previously failed)", ct: ct);

            // Reset the agent status so it can run cleanly
            context.AgentStatuses[agentType] = AgentStatus.Running;

            var success = await RunAgentWithHealingAsync(context, agent, ct);
            completedAgents[agentType] = success;

            if (success)
            {
                _logger.LogInformation("[Daemon] {Agent} succeeded on retry", agentType);
                await PublishEvent(context, agentType, AgentStatus.Completed,
                    $"{agent.Name} recovered on retry", ct: ct);

                // Re-evaluate blocked items now that this agent succeeded
                var unblocked = ReevaluateBlockedItems(context);
                if (unblocked > 0)
                    _logger.LogInformation("[Daemon] Unblocked {Count} items after {Agent} retry succeeded",
                        unblocked, agentType);
            }
            else
            {
                _logger.LogWarning("[Daemon] {Agent} failed again on retry — giving up", agentType);
            }
        }

        // Final backlog re-evaluation after all retries
        var finalUnblocked = ReevaluateBlockedItems(context);
        if (finalUnblocked > 0)
            _logger.LogInformation("[Daemon] Post-retry unblocked {Count} items total", finalUnblocked);
    }

    // ─── Single Agent Runner with Self-Healing ──────────────────────
    // Runs an agent up to MaxAgentRetries. On failure triggers
    // BugFix → Performance → Review heal cycle, then retries.
    // Returns true on success, false if exhausted.

    private async Task<bool> RunAgentWithHealingAsync(AgentContext context, IAgent agent, CancellationToken ct)
    {
        string? lastError = null;

        for (var attempt = 0; attempt <= MaxAgentRetries; attempt++)
        {
            if (ct.IsCancellationRequested) return false;

            if (attempt > 0)
            {
                _logger.LogWarning("[Self-Heal] {Agent} retry {Attempt}/{Max} — {Error}",
                    agent.Name, attempt, MaxAgentRetries, lastError);
                await Task.Delay(RetryDelay * attempt, ct);
            }

            var sw = Stopwatch.StartNew();
            var statusMsg = attempt > 0
                ? $"{agent.Name} self-healing (attempt {attempt + 1}, fixing: {Truncate(lastError, 80)})..."
                : $"{agent.Name} starting...";

            await PublishEvent(context, agent.Type, AgentStatus.Running, statusMsg, attempt, ct: ct);

            // ── Audit: agent started
            await _audit.LogAsync(agent.Type, context.RunId,
                attempt > 0 ? AuditAction.AgentRetried : AuditAction.AgentStarted,
                statusMsg,
                $"Agent: {agent.Name}, Attempt: {attempt + 1}/{MaxAgentRetries + 1}",
                severity: attempt > 0 ? AuditSeverity.Warning : AuditSeverity.Info, ct: ct);

            AgentResult result;
            List<ExpandedRequirement> claimedBatch = [];
            try
            {
                var agentName = agent.Type.ToString();

                // Meta-agents (Backlog, Supervisor, Review, etc.) track/review items
                // but don't produce deliverables — skip lifecycle to avoid stealing items
                if (s_metaAgents.Contains(agent.Type))
                {
                    claimedBatch = [];
                }
                else
                {
                    // ── Stage 1: CLAIM — InQueue → Received ──
                    claimedBatch = _lifecycle.Claim(
                        context.ExpandedRequirements,
                        agentName,
                        item => GetRelevantTaskAgents(item),
                        item => MatchesAgent(item, agent.Type));

                    // ── Stage 2: START — Received → InProgress ──
                    // Items appear as "In Dev" on the dashboard immediately,
                    // even while waiting for HITL approval below.
                    _lifecycle.Start(claimedBatch, agentName);
                }

                // ── HITL: gate for Database DDL execution (only prompt once per run)
                // Items are already InProgress so the dashboard shows them as "In Dev"
                // while waiting for human approval.
                if (agent.Type == AgentType.Database && context.PipelineConfig.ExecuteDdl && !context.DdlApprovedForRun && attempt == 0)
                {
                    var ddlDecision = await _humanGate.RequestApprovalAsync(new HumanApprovalRequest
                    {
                        RunId = context.RunId,
                        RequestingAgent = agent.Type,
                        Category = HumanGateCategory.DatabaseDdl,
                        Title = "Database DDL execution requires approval",
                        Description = $"DatabaseAgent will execute DDL scripts against {context.PipelineConfig.DbHost}:{context.PipelineConfig.DbPort}/{context.PipelineConfig.DbName}. " +
                            "This will create/alter database tables and may affect existing data.",
                        Details = $"Docker: SpinUp={context.PipelineConfig.SpinUpDocker}, ExecuteDdl={context.PipelineConfig.ExecuteDdl}, " +
                            $"Container: {context.PipelineConfig.DockerContainerName}",
                        Timeout = TimeSpan.FromMinutes(15)
                    }, ct);

                    if (ddlDecision == HumanDecision.Approved)
                    {
                        context.DdlApprovedForRun = true;
                        _logger.LogInformation("[HITL] Human APPROVED DDL execution for run {RunId}", context.RunId);
                        await _audit.LogAsync(agent.Type, context.RunId, AuditAction.HumanApproved,
                            "DDL execution approved by human operator", severity: AuditSeverity.Decision, ct: ct);
                    }
                    else if (ddlDecision == HumanDecision.Rejected)
                    {
                        _logger.LogWarning("[HITL] Human REJECTED DDL execution — skipping Database agent");
                        await _audit.LogAsync(agent.Type, context.RunId, AuditAction.HumanRejected,
                            "DDL execution rejected by human operator", severity: AuditSeverity.Decision, ct: ct);
                        context.PipelineConfig.ExecuteDdl = false;
                    }
                    else // TimedOut
                    {
                        _logger.LogWarning("[HITL] DDL approval timed out — disabling DDL execution");
                        await _audit.LogAsync(agent.Type, context.RunId, AuditAction.HumanRejected,
                            "DDL approval timed out — DDL execution disabled for safety", severity: AuditSeverity.Warning, ct: ct);
                        context.PipelineConfig.ExecuteDdl = false;
                    }
                }

                // ── Wire up agent-owned lifecycle delegates ──
                // Agents call CompleteWorkItem / FailWorkItem for each item they process.
                context.CurrentClaimedItems = claimedBatch;
                context.CompleteWorkItem = item => _lifecycle.CompleteItem(
                    item, agentName, context.ExpandedRequirements,
                    i => GetRelevantTaskAgents(i), _taskTracker);
                context.FailWorkItem = (item, reason) => _lifecycle.FailItem(
                    item, agentName, reason, _taskTracker);

                await PrimeAgentWithLlmAsync(context, agent, ct);
                result = await agent.ExecuteAsync(context, ct);
                context.StampProjectScope();

                // Deduplicate artifacts and findings after each agent run to prevent
                // unbounded growth when agents are re-queued by the daemon loop.
                context.DeduplicateArtifacts();
                context.DeduplicateFindings();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await PublishEvent(context, agent.Type, AgentStatus.Failed,
                    $"{agent.Name} cancelled", attempt, elapsed: sw.Elapsed.TotalMilliseconds, ct: ct);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Self-Heal] {Agent} crashed — {ExType}: {Message}\n{StackTrace}",
                    agent.Name, ex.GetType().FullName, ex.Message, ex.StackTrace);

                // Classify: non-recoverable errors should not be retried
                var nonRecoverable = IsNonRecoverableException(ex);
                if (nonRecoverable)
                    _logger.LogWarning("[Self-Heal] {Agent} error classified as non-recoverable — skipping further retries", agent.Name);

                result = new AgentResult
                {
                    Agent = agent.Type, Success = false,
                    Errors = [ex.ToString()],
                    Summary = $"{agent.Name} crashed: {ex.GetType().Name}: {ex.Message}",
                    Duration = sw.Elapsed
                };
                context.AgentStatuses[agent.Type] = AgentStatus.Failed;

                // Store structured failure record immediately for pattern analysis
                context.FailureRecords.Add(new AgentFailureRecord
                {
                    FailedAgent = agent.Type,
                    Attempt = attempt + 1,
                    Error = ex.Message,
                    Summary = result.Summary,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    StackTrace = ex.StackTrace ?? string.Empty,
                    NonRecoverable = nonRecoverable
                });

                if (nonRecoverable)
                {
                    await PublishEvent(context, agent.Type, AgentStatus.Failed,
                        $"{agent.Name} failed (non-recoverable): {ex.GetType().Name}: {ex.Message}",
                        attempt, elapsed: sw.Elapsed.TotalMilliseconds, ct: ct);
                    // Fail only claimed items, not all items in the pipeline
                    foreach (var item in claimedBatch.Where(i => i.Status is WorkItemStatus.InProgress or WorkItemStatus.Received))
                        _lifecycle.FailItem(item, agent.Type.ToString(), $"Non-recoverable: {ex.GetType().Name}: {ex.Message}", _taskTracker);
                    return false;
                }
            }

            // Respect the agent's own status if it set one (e.g. Backlog sets Idle when items remain)
            var reportedStatus = result.Success
                ? (context.AgentStatuses.TryGetValue(agent.Type, out var agentSelf) && agentSelf != AgentStatus.Running
                    ? agentSelf
                    : AgentStatus.Completed)
                : AgentStatus.Failed;

            await PublishEvent(context, agent.Type,
                reportedStatus,
                result.Summary, attempt,
                result.Artifacts.Count, result.Findings.Count,
                sw.Elapsed.TotalMilliseconds, result.TestDiagnostics, ct);

            if (!s_metaAgents.Contains(agent.Type))
            {
                // Agents own their lifecycle: they call CompleteWorkItem/FailWorkItem per item.
                // Safety net: handle any claimed items the agent left InProgress/Received.
                var orphaned = claimedBatch
                    .Where(i => i.Status is WorkItemStatus.InProgress or WorkItemStatus.Received)
                    .ToList();

                if (result.Success)
                {
                    if (orphaned.Count > 0)
                    {
                        _logger.LogWarning("[Lifecycle] {Agent} returned success but left {Count} claimed items unprocessed — failing them",
                            agent.Name, orphaned.Count);
                        foreach (var item in orphaned)
                            _lifecycle.FailItem(item, agent.Type.ToString(),
                                "Agent returned success without completing this item", _taskTracker);
                    }
                }
                else
                {
                    // Agent failed — fail all remaining claimed items
                    foreach (var item in orphaned)
                        _lifecycle.FailItem(item, agent.Type.ToString(),
                            result.Summary ?? "Agent execution failed", _taskTracker);

                    var retriable = claimedBatch.Count(i => i.Status == WorkItemStatus.InQueue);
                    if (retriable > 0)
                        _logger.LogInformation("[Self-Heal] {Agent} attempt {Attempt}: {Retriable} items queued for retry",
                            agent.Name, attempt + 1, retriable);
                }
            }

            // ── Bidirectional artifact ↔ work item linkage ──
            // After a successful non-meta agent run, link generated artifacts to
            // the claimed work items (and vice versa) so traceability is complete.
            if (result.Success && !s_metaAgents.Contains(agent.Type)
                && result.Artifacts.Count > 0 && claimedBatch.Count > 0)
            {
                var completedItems = claimedBatch
                    .Where(i => i.Status == WorkItemStatus.Completed)
                    .ToList();

                if (completedItems.Count > 0)
                {
                    var artifactPaths = result.Artifacts
                        .Select(a => a.RelativePath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var workItemIds = completedItems
                        .Select(i => i.Id)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .ToList();

                    // Link artifacts → work items
                    foreach (var artifact in result.Artifacts)
                    {
                        foreach (var id in workItemIds)
                        {
                            if (!artifact.TracedRequirementIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                                artifact.TracedRequirementIds.Add(id);
                        }
                    }

                    // Link work items → artifacts
                    foreach (var item in completedItems)
                    {
                        foreach (var path in artifactPaths)
                        {
                            if (!item.MatchingArtifactPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                                item.MatchingArtifactPaths.Add(path);
                        }
                    }

                    _logger.LogInformation(
                        "[Traceability] {Agent} linked {ArtifactCount} artifacts ↔ {ItemCount} work items",
                        agent.Name, artifactPaths.Count, completedItems.Count);
                }
            }

            // ── Store result so downstream agents can inspect predecessor outputs ──
            context.AgentResults[agent.Type] = result;

            if (context.PipelineConfig?.EnableAgentCommunicationLogging == true)
            {
                context.CommunicationLog.Add(new AgentCommunicationEntry
                {
                    RunId = context.RunId,
                    ProjectId = context.ProjectId,
                    CommType = AgentCommType.StoreResult,
                    FromAgent = agent.Type,
                    ToAgent = null,
                    Message = result.Success
                        ? $"Stored result: {result.Artifacts.Count} artifacts, {result.Summary?.Length ?? 0} char summary"
                        : $"Stored failed result: {result.Summary?[..Math.Min(200, result.Summary?.Length ?? 0)]}",
                    ItemCount = result.Artifacts.Count
                });
            }

            // ── Record quality metrics for tracking ──
            context.QualityMetrics.Add(new QualityMetric
            {
                Source = agent.Type,
                Category = "AgentExecution",
                Description = $"{agent.Name} execution",
                Value = result.Success ? 1.0 : 0.0,
                Target = 1.0,
                ArtifactPath = $"agents/{agent.Type}",
                RecordedAt = DateTimeOffset.UtcNow
            });
            if (result.Artifacts.Count > 0)
            {
                context.QualityMetrics.Add(new QualityMetric
                {
                    Source = agent.Type,
                    Category = "ArtifactCount",
                    Description = $"{agent.Name} artifacts produced",
                    Value = result.Artifacts.Count,
                    Target = 1.0,
                    ArtifactPath = $"agents/{agent.Type}/artifacts",
                    RecordedAt = DateTimeOffset.UtcNow
                });
            }

            // ── Audit: agent completed/failed with details
            await _audit.LogAsync(agent.Type, context.RunId,
                result.Success ? AuditAction.AgentCompleted : AuditAction.AgentFailed,
                result.Success
                    ? $"{agent.Name} completed: {result.Summary}"
                    : $"{agent.Name} failed: {result.Summary}",
                $"Artifacts: {result.Artifacts.Count}, Findings: {result.Findings.Count}, " +
                $"Errors: [{string.Join("; ", result.Errors)}], Duration: {sw.Elapsed.TotalMilliseconds:F0}ms",
                severity: result.Success ? AuditSeverity.Info : AuditSeverity.Warning, ct: ct);

            // ── Audit: individual findings raised
            foreach (var finding in result.Findings)
            {
                var sev = finding.Severity is ReviewSeverity.SecurityViolation or ReviewSeverity.ComplianceViolation
                    ? AuditSeverity.SecurityEvent
                    : finding.Severity == ReviewSeverity.Critical ? AuditSeverity.Critical
                    : AuditSeverity.Info;

                await _audit.LogAsync(agent.Type, context.RunId, AuditAction.FindingRaised,
                    $"Finding [{finding.Severity}]: {Truncate(finding.Message, 120)}",
                    $"File: {finding.FilePath}:{finding.LineNumber}, Category: {finding.Category}, Suggestion: {finding.Suggestion}",
                    severity: sev, ct: ct);
            }

            // ── HITL gate: critical findings require human approval to continue
            if (result.Findings.Any(f =>
                f.Severity is ReviewSeverity.SecurityViolation or ReviewSeverity.ComplianceViolation))
            {
                var criticalFindings = result.Findings
                    .Where(f => f.Severity is ReviewSeverity.SecurityViolation or ReviewSeverity.ComplianceViolation)
                    .ToList();

                var decision = await _humanGate.RequestApprovalAsync(new HumanApprovalRequest
                {
                    RunId = context.RunId,
                    RequestingAgent = agent.Type,
                    Category = criticalFindings.Any(f => f.Severity == ReviewSeverity.SecurityViolation)
                        ? HumanGateCategory.SecurityViolation
                        : HumanGateCategory.ComplianceViolation,
                    Title = $"{agent.Name}: {criticalFindings.Count} critical finding(s) require review",
                    Description = $"Agent {agent.Name} raised {criticalFindings.Count} security/compliance finding(s). Review required before pipeline continues.",
                    Details = string.Join("\n", criticalFindings.Select(f =>
                        $"[{f.Severity}] {f.Message} — {f.FilePath}:{f.LineNumber}")),
                    Timeout = TimeSpan.FromMinutes(30)
                }, ct);

                if (decision == HumanDecision.Rejected)
                {
                    _logger.LogWarning("[HITL] Human REJECTED continuation after {Agent} — treating as failure", agent.Name);
                    return false;
                }
            }

            if (result.Success)
            {
                if (attempt > 0)
                {
                    _logger.LogInformation("[Self-Heal] {Agent} recovered on attempt {Attempt}", agent.Name, attempt + 1);
                    context.TestDiagnostics.Add(new TestDiagnostic
                    {
                        TestName = $"SelfHeal_{agent.Type}",
                        AgentUnderTest = agent.Type.ToString(),
                        Outcome = TestOutcome.Remediated,
                        Diagnostic = $"Failed with: {lastError}",
                        Remediation = $"Self-healed on attempt {attempt + 1}",
                        Category = "SelfHealing",
                        DurationMs = sw.Elapsed.TotalMilliseconds,
                        AttemptNumber = attempt + 1
                    });
                }
                return true;
            }

            // Record failure context
            lastError = result.Errors.FirstOrDefault() ?? result.Summary;
            var isNonRecoverable = IsNonRecoverableError(lastError);
            context.RetryAttempts[result.Agent] = context.RetryAttempts.GetValueOrDefault(result.Agent, 0) + 1;
            context.FailureRecords.Add(new AgentFailureRecord
            {
                FailedAgent = agent.Type,
                Attempt = attempt + 1,
                Error = lastError ?? "Unknown",
                Summary = result.Summary,
                NonRecoverable = isNonRecoverable
            });

            // Skip retries for non-recoverable errors (auth failures, missing config, etc.)
            if (isNonRecoverable)
            {
                _logger.LogWarning("[Self-Heal] {Agent} error is non-recoverable — skipping remaining retries: {Error}",
                    agent.Name, lastError);
                break;
            }

            // Heal cycle for non-heal agents
            if (!s_healCycleAgents.Contains(agent.Type))
            {
                await RunHealCycleAsync(context, agent.Type, lastError, attempt + 1, ct);
            }
        }

        // Exhausted retries — mark failed and re-queue uncompleted items via lifecycle policy
        _logger.LogWarning("[Self-Heal] {Agent} FAILED after {Max} attempts — re-queuing backlog items", agent.Name, MaxAgentRetries + 1);
        context.TestDiagnostics.Add(new TestDiagnostic
        {
            TestName = $"SelfHeal_{agent.Type}_Skipped",
            AgentUnderTest = agent.Type.ToString(),
            Outcome = TestOutcome.Failed,
            Diagnostic = $"Exhausted {MaxAgentRetries + 1} attempts: {lastError}",
            Remediation = "Agent failed — uncompleted backlog items re-queued via lifecycle policy",
            Category = "SelfHealing",
            DurationMs = 0,
            AttemptNumber = MaxAgentRetries + 1
        });
        _lifecycle.Fail(context.ExpandedRequirements, agent.Type.ToString(), _taskTracker);
        context.AgentStatuses[agent.Type] = AgentStatus.Failed;
        return false;
    }

    // ─── Build → Fix → Rebuild Cycle ────────────────────────────────

    private async Task<bool> RunBuildFixCycleAsync(AgentContext context, IAgent buildAgent, CancellationToken ct)
    {
        var cycle = 0;
        var lastErrorCount = int.MaxValue;
        var stalledRounds = 0;
        const int maxStalledRounds = 3; // stop only if BugFix makes zero progress for N consecutive rounds

        while (!ct.IsCancellationRequested)
        {
            cycle++;
            _logger.LogInformation("[Build-Fix] Cycle {Cycle} (no cap — runs until green)", cycle);

            await PublishEvent(context, AgentType.Build, AgentStatus.Running,
                $"Build cycle {cycle} — compiling generated solution...", ct: ct);

            await PrimeAgentWithLlmAsync(context, buildAgent, ct);
            var buildResult = await buildAgent.ExecuteAsync(context, ct);
            context.StampProjectScope();

            await PublishEvent(context, AgentType.Build,
                buildResult.Success ? AgentStatus.Completed : AgentStatus.Failed,
                buildResult.Summary, ct: ct);

            if (buildResult.Success)
            {
                _logger.LogInformation("[Build-Fix] Build succeeded on cycle {Cycle}", cycle);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Build passed on cycle {cycle} — proceeding to deployment", ct: ct);

                await _audit.LogAsync(AgentType.Build, context.RunId, AuditAction.AgentCompleted,
                    $"Build succeeded on cycle {cycle}",
                    $"Errors: 0, Findings: {buildResult.Findings.Count}",
                    severity: AuditSeverity.Info, ct: ct);
                return true;
            }

            // Build failed — collect error findings and create backlog bugs
            var buildErrors = buildResult.Findings
                .Where(f => f.Category == "Build" && f.Severity >= ReviewSeverity.Error)
                .ToList();

            _logger.LogWarning("[Build-Fix] Build failed with {Count} errors — dispatching to BugFix (cycle {Cycle})", buildErrors.Count, cycle);
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                $"Build failed with {buildErrors.Count} errors — dispatching BugFix agent (cycle {cycle})...", ct: ct);

            await _audit.LogAsync(AgentType.Build, context.RunId, AuditAction.AgentFailed,
                $"Build failed on cycle {cycle}: {buildErrors.Count} errors",
                $"Errors: {string.Join("; ", buildErrors.Take(5).Select(e => Truncate(e.Message, 80)))}",
                severity: AuditSeverity.Warning, ct: ct);

            // Create Bug backlog items from build errors so they appear on the dashboard
            var bugsCreated = 0;
            var existingBuildBugIds = context.ExpandedRequirements
                .Where(e => e.ItemType == WorkItemType.Bug && e.Tags.Contains("build"))
                .Select(e => e.SourceRequirementId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var err in buildErrors)
            {
                var sourceRef = $"BUILD:{err.Id}";
                if (existingBuildBugIds.Contains(sourceRef)) continue;
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"BUG-BLD-{err.Id[..Math.Min(8, err.Id.Length)].ToUpperInvariant()}",
                    SourceRequirementId = sourceRef,
                    ItemType = WorkItemType.Bug,
                    Title = $"[Build] {Truncate(err.Message, 80)}",
                    Description = $"Build error in {err.FilePath}:\n{err.Message}",
                    Module = ExtractModuleFromPath(err.FilePath),
                    Priority = 1, // build errors are blockers
                    Iteration = context.DevIteration,
                    Tags = ["bugfix", "build"],
                    Status = WorkItemStatus.InQueue,
                    ProducedBy = "Build",
                });
                existingBuildBugIds.Add(sourceRef);
                bugsCreated++;
            }
            if (bugsCreated > 0)
                _logger.LogInformation("[Build-Fix] Created {Count} Bug backlog items from build errors", bugsCreated);

            // Stall detection: if error count hasn't decreased, BugFix isn't making progress
            if (buildErrors.Count >= lastErrorCount)
            {
                stalledRounds++;
                if (stalledRounds >= maxStalledRounds)
                {
                    _logger.LogWarning("[Build-Fix] No progress for {Rounds} consecutive rounds ({Errors} errors remain) — stopping build-fix loop",
                        stalledRounds, buildErrors.Count);
                    await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Failed,
                        $"Build-Fix stalled after {cycle} cycles — {buildErrors.Count} errors remain, BugFix made no progress for {stalledRounds} rounds. DEPLOYMENT BLOCKED", ct: ct);
                    await _audit.LogAsync(AgentType.Build, context.RunId, AuditAction.AgentFailed,
                        $"Build stalled after {cycle} cycles — deployment blocked (no progress)",
                        severity: AuditSeverity.Critical, ct: ct);
                    return false;
                }
            }
            else
            {
                stalledRounds = 0; // progress made — reset counter
            }
            lastErrorCount = buildErrors.Count;

            // Run BugFix to attempt code repairs
            var bugFix = _agents.FirstOrDefault(a => a.Type == AgentType.BugFix);
            if (bugFix is not null)
            {
                await PublishEvent(context, AgentType.BugFix, AgentStatus.Running,
                    $"BugFix repairing {buildErrors.Count} build errors (cycle {cycle})...", ct: ct);
                await RunSingleHealAgent(context, bugFix,
                    $"BugFix repairing {buildErrors.Count} build errors from build cycle {cycle}", ct);
            }
            else
            {
                _logger.LogWarning("[Build-Fix] BugFix agent not available — cannot auto-repair");
                return false;
            }
        }
        return false;
    }

    /// <summary>Extract a module name from a file path for bug items.</summary>
    private static string ExtractModuleFromPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "General";
        if (filePath.Contains("Database", StringComparison.OrdinalIgnoreCase)) return "Database";
        if (filePath.Contains("Test", StringComparison.OrdinalIgnoreCase)) return "Testing";
        if (filePath.Contains("Integration", StringComparison.OrdinalIgnoreCase)) return "Integration";
        // Try to extract service name from path like .../PatientService/...
        var parts = filePath.Replace('\\', '/').Split('/');
        var svcPart = parts.FirstOrDefault(p => p.EndsWith("Service", StringComparison.OrdinalIgnoreCase));
        if (svcPart is not null) return svcPart;
        return "General";
    }

    // ─── Monitor → Fix → Redeploy Cycle ────────────────────────────

    private async Task RunMonitorFixCycleAsync(
        AgentContext context, IAgent monitorAgent, IAgent deployAgent, CancellationToken ct)
    {
        const int maxMonitorCycles = 2;

        for (var cycle = 0; cycle < maxMonitorCycles; cycle++)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("[Monitor-Fix] Cycle {Cycle}/{Max}", cycle + 1, maxMonitorCycles);

            await PublishEvent(context, AgentType.Monitor, AgentStatus.Running,
                $"Monitor cycle {cycle + 1}/{maxMonitorCycles} — checking containers and services...", ct: ct);

            await PrimeAgentWithLlmAsync(context, monitorAgent, ct);
            var monitorResult = await monitorAgent.ExecuteAsync(context, ct);
            context.StampProjectScope();

            await PublishEvent(context, AgentType.Monitor,
                monitorResult.Success ? AgentStatus.Completed : AgentStatus.Failed,
                monitorResult.Summary, ct: ct);

            await _audit.LogAsync(AgentType.Monitor, context.RunId,
                monitorResult.Success ? AuditAction.AgentCompleted : AuditAction.AgentFailed,
                monitorResult.Summary,
                $"Issues: {monitorResult.Findings.Count}, Errors: {monitorResult.Errors.Count}",
                severity: monitorResult.Success ? AuditSeverity.Info : AuditSeverity.Warning, ct: ct);

            if (monitorResult.Success)
            {
                _logger.LogInformation("[Monitor-Fix] All services healthy on cycle {Cycle}", cycle + 1);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Monitor: all services healthy — deployment verified on cycle {cycle + 1}", ct: ct);
                return;
            }

            // Monitor detected issues — attempt remediation
            var monitorIssues = monitorResult.Findings.Count;
            _logger.LogWarning("[Monitor-Fix] Detected {Count} issues — attempting remediation", monitorIssues);
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                $"Monitor detected {monitorIssues} issues — dispatching BugFix + Redeploy (cycle {cycle + 1})...", ct: ct);

            // Dispatch BugFix for deployment/runtime errors
            var bugFix = _agents.FirstOrDefault(a => a.Type == AgentType.BugFix);
            if (bugFix is not null)
            {
                await RunSingleHealAgent(context, bugFix,
                    $"BugFix addressing {monitorIssues} monitor-detected issues (cycle {cycle + 1})", ct);
            }

            // Rebuild if code was changed
            var buildAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Build);
            if (buildAgent is not null)
            {
                await RunBuildFixCycleAsync(context, buildAgent, ct);
            }

            // Redeploy after fixes
            await PublishEvent(context, AgentType.Deploy, AgentStatus.Running,
                $"Redeploying after monitor-fix cycle {cycle + 1}...", ct: ct);
            await RunAgentWithHealingAsync(context, deployAgent, ct);

            if (cycle + 1 >= maxMonitorCycles)
            {
                _logger.LogWarning("[Monitor-Fix] Exhausted {Max} monitor-fix cycles — issues may remain", maxMonitorCycles);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Monitor-Fix cycle exhausted ({maxMonitorCycles} attempts) — some issues may persist", ct: ct);
            }
        }
    }

    // ─── Heal Cycle: BugFix → Performance → Review ─────────────────

    private async Task RunHealCycleAsync(
        AgentContext context, AgentType failedAgent, string? error, int attempt, CancellationToken ct)
    {
        _logger.LogInformation("[Heal-Cycle] {Agent} failed (attempt {Attempt}) — BugFix → Performance → Review",
            failedAgent, attempt);

        context.Findings.Add(new ReviewFinding
        {
            Category = "Implementation",
            Severity = ReviewSeverity.Error,
            Message = $"Agent {failedAgent} failed: {error}",
            ArtifactId = failedAgent.ToString(),
            Suggestion = $"Diagnose and fix the root cause of {failedAgent} failure"
        });

        var bugFix = _agents.FirstOrDefault(a => a.Type == AgentType.BugFix);
        if (bugFix is not null)
            await RunSingleHealAgent(context, bugFix, $"BugFix repairing {failedAgent} (attempt {attempt})", ct);

        var perf = _agents.FirstOrDefault(a => a.Type == AgentType.Performance);
        if (perf is not null)
            await RunSingleHealAgent(context, perf, $"Performance checking {failedAgent} fix", ct);

        var review = _agents.FirstOrDefault(a => a.Type == AgentType.Review);
        if (review is not null)
            await RunSingleHealAgent(context, review, $"Review validating {failedAgent} fix", ct);
    }

    private async Task RunSingleHealAgent(AgentContext context, IAgent agent, string message, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await PublishEvent(context, agent.Type, AgentStatus.Running, message, ct: ct);

        AgentResult result;
        try
        {
            await PrimeAgentWithLlmAsync(context, agent, ct);
            result = await agent.ExecuteAsync(context, ct);
            context.StampProjectScope();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Heal-Cycle] {Agent} failed — continuing", agent.Name);
            result = new AgentResult
            {
                Agent = agent.Type, Success = false,
                Errors = [ex.Message], Summary = $"{agent.Name} heal-cycle error: {ex.Message}"
            };
        }

        await PublishEvent(context, agent.Type,
            result.Success ? AgentStatus.Completed : AgentStatus.Failed,
            result.Summary, elapsed: sw.Elapsed.TotalMilliseconds,
            diagnostics: result.TestDiagnostics, ct: ct);
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private async Task PrimeAgentWithLlmAsync(AgentContext context, IAgent agent, CancellationToken ct)
    {
        if (!_llm.IsAvailable)
            return;

        var activeItems = context.ExpandedRequirements.Count(r =>
            r.Status is WorkItemStatus.InQueue or WorkItemStatus.Received or WorkItemStatus.InProgress);
        var reqSample = context.Requirements
            .Take(8)
            .Select(r => $"- [{r.Id}] {r.Title}")
            .ToList();
        var promptBody = reqSample.Count > 0
            ? string.Join("\n", reqSample)
            : "- No parsed requirements currently available.";

        var prompt = new LlmPrompt
        {
            RequestingAgent = $"Orchestrator:{agent.Type}",
            Temperature = 0.1,
            MaxTokens = 450,
            SystemPrompt = "You are an SDLC execution copilot. Return concise, actionable guidance for a single software delivery agent.",
            UserPrompt = $"""
Create a short execution brief for agent {agent.Type}.

Run context:
- RunId: {context.RunId}
- ProjectId: {context.ProjectId ?? "n/a"}
- TotalRequirements: {context.Requirements.Count}
- ActiveWorkItems: {activeItems}
- ExistingArtifacts: {context.Artifacts.Count}
- ExistingFindings: {context.Findings.Count}

Requirement sample:
{promptBody}

Return exactly 3 sections in markdown:
1) Objective
2) Steps
3) RisksToWatch
"""
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
                return;

            context.Messages.Add(new AgentMessage
            {
                From = AgentType.Orchestrator,
                To = agent.Type,
                Subject = "LLM Execution Brief",
                Body = response.Content.Trim(),
                Metadata = new Dictionary<string, object>
                {
                    ["provider"] = _llm.ProviderName,
                    ["model"] = response.Model,
                    ["promptTokens"] = response.PromptTokens,
                    ["completionTokens"] = response.CompletionTokens
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate LLM execution brief for agent {Agent}", agent.Type);
        }
    }

    private static AgentType[] GetDependencies(AgentType agent) =>
        s_dependencies.TryGetValue(agent, out var deps) ? deps : [];

    /// <summary>
    /// Returns agent types that should be excluded based on the project domain.
    /// Domain-specific agents (e.g. HIPAA) are only relevant for certain domains;
    /// universal agents (e.g. SOC2) run for all projects.
    /// </summary>
    private static HashSet<AgentType> GetDomainExcludedAgents(PipelineConfig config)
    {
        var excluded = new HashSet<AgentType>();
        var domain = config.ProjectDomain ?? "";

        // HIPAA compliance — only for Healthcare / Medical / Clinical / Pharma domains
        var isHealthcare = domain.Contains("health", StringComparison.OrdinalIgnoreCase) ||
                           domain.Contains("medical", StringComparison.OrdinalIgnoreCase) ||
                           domain.Contains("clinical", StringComparison.OrdinalIgnoreCase) ||
                           domain.Contains("pharma", StringComparison.OrdinalIgnoreCase);

        if (!isHealthcare)
            excluded.Add(AgentType.HipaaCompliance);

        // SOC2 is universal — runs for ALL projects (not excluded)
        // Future: add more domain-specific exclusion rules here
        // e.g. PCI-DSS only for FinTech/E-Commerce, FERPA only for Education, etc.

        return excluded;
    }

    private async Task PublishEvent(
        AgentContext context, AgentType agent, AgentStatus status, string message,
        int retryAttempt = 0, int artifactCount = 0, int findingCount = 0,
        double elapsed = 0, List<TestDiagnostic>? diagnostics = null,
        CancellationToken ct = default)
    {
        List<TestDiagnosticDto>? diagDtos = null;
        if (diagnostics is { Count: > 0 })
        {
            diagDtos = diagnostics.Select(d => new TestDiagnosticDto
            {
                TestName = d.TestName, AgentUnderTest = d.AgentUnderTest,
                Outcome = (int)d.Outcome, Diagnostic = d.Diagnostic,
                Remediation = d.Remediation, Category = d.Category,
                DurationMs = d.DurationMs, AttemptNumber = d.AttemptNumber
            }).ToList();
        }

        await _eventSink.OnEventAsync(new PipelineEvent
        {
            RunId = context.RunId,
            Agent = agent,
            Status = status,
            Message = message,
            ArtifactCount = artifactCount,
            FindingCount = findingCount,
            ElapsedMs = elapsed,
            RetryAttempt = retryAttempt,
            TestDiagnostics = diagDtos
        }, ct);
    }

    // ─── Post-Pipeline Instruction Execution ───────────────────────

    private static readonly string[] s_runProjectKeywords =
        ["run the project", "run the solution", "build and run", "dotnet run", "start the project"];

    private static readonly string[] s_buildProjectKeywords =
        ["build the project", "build the solution", "dotnet build", "compile the project"];

    private static readonly string[] s_testProjectKeywords =
        ["run tests", "run the tests", "dotnet test", "execute tests"];

    // ═══════════════════════════════════════════════════════════════
    //  Project Tech Stack — Load from DB
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads the project's tech stack from <c>ProjectTechStack</c> table and populates
    /// <see cref="AgentContext.ResolvedTechStack"/> so all agents can read technology
    /// versions dynamically instead of hardcoding them.
    /// </summary>
    private async Task LoadProjectTechStackAsync(IServiceScope scope, AgentContext context, string projectId, CancellationToken ct)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<GNex.Database.GNexDbContext>();
            var techEntries = await db.ProjectTechStacks
                .Where(t => t.ProjectId == projectId)
                .ToListAsync(ct);

            if (techEntries.Count == 0)
            {
                _logger.LogDebug("No tech stack entries found for project {ProjectId}, agents will use defaults", projectId);
                return;
            }

            context.ResolvedTechStack = techEntries.Select(t => new ResolvedTechStackEntry
            {
                TechnologyId = t.TechnologyId,
                TechnologyName = t.TechnologyId, // TechnologyId is the name (e.g. "C#", ".NET", "PostgreSQL")
                TechnologyType = t.TechnologyType,
                Layer = t.Layer,
                Version = t.Version ?? string.Empty,
                ConfigOverridesJson = t.ConfigOverridesJson
            }).ToList();

            _logger.LogInformation("Loaded {Count} tech stack entries for project {ProjectId}: {Summary}",
                techEntries.Count, projectId,
                string.Join(", ", context.ResolvedTechStack.Select(t => $"{t.TechnologyName} {t.Version}".Trim())));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tech stack for project {ProjectId} — agents will use defaults", projectId);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Persistent Learning — Load / Harvest / Save
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Load historical learnings (project + domain + global) at pipeline start.
    /// Injects them into the AgentContext so PromptGenerator and code-gen agents can consume them.
    /// </summary>
    private async Task LoadHistoricalLearningsAsync(AgentContext context, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetService<ILearningRepository>();
            if (repo is null) return;

            var projectId = context.ProjectId ?? string.Empty;
            var domain = context.DomainProfile?.Domain ?? string.Empty;

            var dbLearnings = await repo.GetCombinedForPipelineAsync(projectId, domain, 150, ct);

            if (dbLearnings.Count == 0) return;

            // Convert DB entities → in-memory AgentLearningRecords
            var records = dbLearnings.Select(l => new AgentLearningRecord
            {
                Id = l.Id,
                RunId = l.RunId,
                ProjectId = l.ProjectId,
                AgentType = l.AgentTypeCode,
                Scope = (LearningScope)l.Scope,
                Category = l.Category,
                Problem = l.Problem,
                Resolution = l.Resolution,
                Impact = l.Impact,
                TargetAgents = string.IsNullOrEmpty(l.TargetAgents) ? [] : [.. l.TargetAgents.Split(',')],
                PromptRule = l.PromptRule,
                Domain = l.Domain,
                SeenInProjects = string.IsNullOrEmpty(l.SeenInProjects) ? [] : [.. l.SeenInProjects.Split(',')],
                SeenInDomains = string.IsNullOrEmpty(l.SeenInDomains) ? [] : [.. l.SeenInDomains.Split(',')],
                Confidence = l.Confidence,
                IsVerified = l.IsVerified,
                IsDeprecated = l.IsDeprecated,
                Recurrence = l.Recurrence
            });

            context.InjectHistoricalLearnings(records);
            _logger.LogInformation("Loaded {Count} historical learnings (project: {P}, domain: {D}, global: {G})",
                dbLearnings.Count,
                dbLearnings.Count(l => l.Scope == 0),
                dbLearnings.Count(l => l.Scope == 1),
                dbLearnings.Count(l => l.Scope == 2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load historical learnings — pipeline will proceed without them");
        }
    }

    /// <summary>
    /// Harvest learnings from the completed pipeline and persist them to DB.
    /// Also verifies learnings from previous runs whose problems did not recur.
    /// </summary>
    private async Task PersistLearningsAsync(AgentContext context, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetService<ILearningRepository>();
            if (repo is null) return;

            // 1. Harvest new learnings from this run
            var harvested = context.HarvestLearnings();
            if (harvested.Count > 0)
            {
                var domain = context.DomainProfile?.Domain ?? string.Empty;
                var dbEntities = harvested.Select(l => new AgentLearning
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = l.ProjectId ?? string.Empty,
                    RunId = l.RunId,
                    AgentTypeCode = l.AgentType,
                    Category = l.Category,
                    Problem = l.Problem,
                    Resolution = l.Resolution,
                    Impact = l.Impact,
                    TargetAgents = string.Join(",", l.TargetAgents),
                    PromptRule = l.PromptRule,
                    Domain = domain,
                    Scope = (int)l.Scope,
                    IsActive = true
                });

                await repo.SaveBatchAsync(dbEntities, ct);
                _logger.LogInformation("Persisted {Count} learnings to DB (run {RunId})", harvested.Count, context.RunId);
            }

            // 2. Verify learnings from previous runs that were NOT reproduced
            var verifiedIds = context.IdentifyVerifiedLearnings();
            foreach (var id in verifiedIds)
                await repo.VerifyAsync(id, ct);

            if (verifiedIds.Count > 0)
                _logger.LogInformation("Verified {Count} historical learnings (problems not reproduced)", verifiedIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist learnings — they will be lost for this run");
        }
    }

    /// <summary>
    /// Persist all communication log entries from this pipeline run to the database.
    /// Only runs when <see cref="PipelineConfig.EnableAgentCommunicationLogging"/> is true
    /// and there are log entries to persist.
    /// </summary>
    private async Task PersistCommunicationLogAsync(AgentContext context, CancellationToken ct)
    {
        if (context.PipelineConfig?.EnableAgentCommunicationLogging != true || context.CommunicationLog.IsEmpty)
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetService<GNex.Database.GNexDbContext>();
            if (db is null) return;

            var entities = context.CommunicationLog.Select(e => new GNex.Database.Entities.Platform.AgentCommunicationLog
            {
                Id = e.Id,
                TenantId = "default",
                RunId = e.RunId,
                ProjectId = e.ProjectId,
                CommType = e.CommType.ToString(),
                FromAgent = e.FromAgent.ToString(),
                ToAgent = e.ToAgent?.ToString(),
                Message = e.Message,
                ItemCount = e.ItemCount,
                Category = e.Category,
                EventTimestamp = e.Timestamp,
                CreatedAt = e.Timestamp,
                CreatedBy = "pipeline"
            }).ToList();

            db.AgentCommunicationLogs.AddRange(entities);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Persisted {Count} communication log entries to DB (run {RunId})",
                entities.Count, context.RunId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist communication log — entries will be lost for this run");
        }
    }

    private async Task ExecutePostPipelineInstructions(AgentContext context, PipelineConfig config, CancellationToken ct)
    {
        if (context.OrchestratorInstructions.Count == 0) return;

        var allInstructions = string.Join(" ", context.OrchestratorInstructions).ToLowerInvariant();
        var outputPath = config.OutputPath;

        if (string.IsNullOrEmpty(outputPath) || !Directory.Exists(outputPath))
        {
            _logger.LogWarning("Post-pipeline: output path not available for instruction execution");
            return;
        }

        // Check for "run the project" type instructions
        if (s_runProjectKeywords.Any(k => allInstructions.Contains(k)))
        {
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                "Executing post-pipeline instruction: building and running the project...", ct: ct);

            // Find any .csproj files in the output path
            var slnFiles = Directory.GetFiles(outputPath, "*.sln", SearchOption.AllDirectories);
            var csprojFiles = Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories);

            string? target = slnFiles.FirstOrDefault() ?? csprojFiles.FirstOrDefault();
            if (target is not null)
            {
                await RunShellCommandAsync(context, "dotnet", $"build \"{target}\"",
                    "Building generated project...", ct);
                await RunShellCommandAsync(context, "dotnet", $"run --project \"{target}\" --no-build",
                    "Running generated project...", ct, timeoutMs: 15000);
            }
            else
            {
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
                    "Post-pipeline: No .sln or .csproj found in output — cannot run project", ct: ct);
            }
        }
        else if (s_buildProjectKeywords.Any(k => allInstructions.Contains(k)))
        {
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                "Executing post-pipeline instruction: building the project...", ct: ct);

            var slnFiles = Directory.GetFiles(outputPath, "*.sln", SearchOption.AllDirectories);
            var csprojFiles = Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories);
            string? target = slnFiles.FirstOrDefault() ?? csprojFiles.FirstOrDefault();

            if (target is not null)
                await RunShellCommandAsync(context, "dotnet", $"build \"{target}\"",
                    "Building generated project...", ct);
            else
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
                    "Post-pipeline: No .sln or .csproj found in output — cannot build", ct: ct);
        }
        else if (s_testProjectKeywords.Any(k => allInstructions.Contains(k)))
        {
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                "Executing post-pipeline instruction: running tests...", ct: ct);

            var testProjects = Directory.GetFiles(outputPath, "*Tests*.csproj", SearchOption.AllDirectories);
            foreach (var tp in testProjects.Take(5))
                await RunShellCommandAsync(context, "dotnet", $"test \"{tp}\" --no-restore",
                    $"Running tests: {Path.GetFileName(tp)}...", ct);
        }
        else
        {
            _logger.LogInformation("Post-pipeline instructions present but no actionable commands recognized: {Instructions}",
                string.Join("; ", context.OrchestratorInstructions));
        }
    }

    private async Task RunShellCommandAsync(AgentContext context, string command, string args,
        string statusMessage, CancellationToken ct, int timeoutMs = 120_000)
    {
        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running, statusMessage, ct: ct);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
                    $"Post-pipeline: Failed to start '{command} {args}'", ct: ct);
                return;
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            var success = proc.ExitCode == 0;
            var output = success ? Truncate(stdout, 500) : Truncate(stderr, 500);
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
                $"Post-pipeline [{command}]: exit={proc.ExitCode} — {output}", ct: ct);

            _logger.LogInformation("Post-pipeline command: {Cmd} {Args} → exit {Exit}", command, args, proc.ExitCode);
        }
        catch (OperationCanceledException)
        {
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
                $"Post-pipeline: '{command}' timed out after {timeoutMs}ms", ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Post-pipeline command failed: {Cmd} {Args}", command, args);
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
                $"Post-pipeline: '{command}' error — {ex.Message}", ct: ct);
        }
    }

    private static string Truncate(string? s, int max) =>
        s is null ? "" : s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Classifies whether an exception is non-recoverable (retrying won't help).
    /// Auth failures, config errors, and missing infrastructure are non-recoverable.
    /// </summary>
    private static bool IsNonRecoverableException(Exception ex) =>
        IsNonRecoverableError(ex.Message) ||
        ex is ArgumentException or InvalidOperationException or NotSupportedException or
            UnauthorizedAccessException or System.Security.SecurityException;

    /// <summary>
    /// Classifies whether an error message indicates a non-recoverable problem.
    /// </summary>
    private static bool IsNonRecoverableError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return false;
        // PostgreSQL auth failures — retrying with the same password is futile
        if (error.Contains("28P01", StringComparison.Ordinal)) return true;
        if (error.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase)) return true;
        // Connection refused — infrastructure not available
        if (error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)) return true;
        // Missing configuration
        if (error.Contains("not configured", StringComparison.OrdinalIgnoreCase)) return true;
        if (error.Contains("API key expired", StringComparison.OrdinalIgnoreCase)) return true;
        if (error.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ── Backlog item <-> Agent mapping ──────────────────────────────

    /// <summary>Maps task tags → responsible agent types.</summary>
    private static readonly Dictionary<string, AgentType> s_tagToAgent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["database"]       = AgentType.Database,
        ["service"]        = AgentType.ServiceLayer,
        ["api"]            = AgentType.Application,
        ["application"]    = AgentType.Application,
        ["contract"]       = AgentType.Application,
        ["testing"]        = AgentType.Testing,
        ["e2e"]            = AgentType.Testing,
        ["bugfix"]         = AgentType.BugFix,
        ["integration"]    = AgentType.Integration,
        ["security"]       = AgentType.Security,
        ["hipaa"]          = AgentType.HipaaCompliance,
        ["compliance"]     = AgentType.Soc2Compliance,
        ["performance"]    = AgentType.Performance,
        ["accesscontrol"]  = AgentType.AccessControl,
        ["observability"]  = AgentType.Observability,
        ["infrastructure"] = AgentType.Infrastructure,
        ["documentation"]  = AgentType.ApiDocumentation,
        ["gap-analysis"]   = AgentType.GapAnalysis,
        ["ui"]             = AgentType.UiUx,
        ["frontend"]       = AgentType.UiUx,
        ["blazor"]         = AgentType.UiUx,
        ["razor"]          = AgentType.UiUx,
    };

    /// <summary>Maps agent type → the tags it can claim (reverse of s_tagToAgent).</summary>
    private static readonly Dictionary<AgentType, HashSet<string>> s_agentTags =
        s_tagToAgent.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase));

    /// <summary>Agents that handle Epics/UserStories (not specific tags).</summary>
    private static readonly HashSet<AgentType> s_epicAgents =
    [
        AgentType.RequirementsReader, AgentType.RequirementsExpander, AgentType.Backlog,
        AgentType.Review, AgentType.Supervisor, AgentType.RequirementAnalyzer
    ];

    /// <summary>
    /// Re-evaluate dependency gates and queue admission on every daemon loop:
    /// - Items with unmet dependencies stay in New (pool)
    /// - Items with satisfied dependencies are admitted to InQueue up to queue cap
    /// This keeps lifecycle strict: New (pool) -> InQueue -> Received -> InProgress -> Completed.
    /// </summary>
    private static int ReevaluateBlockedItems(AgentContext context)
    {
        RollupParentItems(context.ExpandedRequirements);

        var maxQueue = context.PipelineConfig?.MaxQueueItems ?? 50;
        var maxInDev = context.PipelineConfig?.MaxInDevItems ?? 50;
        var promotionCap = (maxQueue + maxInDev) * 2;
        var changed = 0;
        var items = context.ExpandedRequirements
            .Where(IsActionableWorkItem)
            .Where(i => i.Status is not (WorkItemStatus.Completed or WorkItemStatus.Received or WorkItemStatus.InProgress))
            .ToList();

        var byId = context.ExpandedRequirements
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var downstreamDependents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in context.ExpandedRequirements)
        {
            foreach (var depId in item.DependsOn)
            {
                if (!byId.ContainsKey(depId)) continue;
                downstreamDependents.TryGetValue(depId, out var count);
                downstreamDependents[depId] = count + 1;
            }
        }

        var ready = items
            .Where(item => item.DependsOn.Count == 0 || item.DependsOn.All(depId =>
            {
                if (!byId.TryGetValue(depId, out var dep)) return true;
                if (!IsActionableWorkItem(dep)) return true;
                if (dep.Status == WorkItemStatus.Completed) return true;

                var ownerAgent = GetOwnerAgent(dep);
                return ownerAgent.HasValue &&
                       context.AgentStatuses.TryGetValue(ownerAgent.Value, out var agentStatus) &&
                       agentStatus == AgentStatus.Failed;
            }))
            .OrderByDescending(item => downstreamDependents.TryGetValue(item.Id, out var c) ? c : 0)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Id)
            .Take(promotionCap)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (ready.Contains(item.Id))
            {
                if (item.Status != WorkItemStatus.InQueue)
                {
                    item.Status = WorkItemStatus.InQueue;
                    changed++;
                }
            }
            // Don't demote InQueue items to New — they may be waiting for an agent to claim them.
            // Only promote New → InQueue, never InQueue → New (avoids thrashing).
        }

        return changed;
    }

    /// <summary>Determine which agent owns a work item based on its tags.</summary>
    private static AgentType? GetOwnerAgent(ExpandedRequirement item)
    {
        foreach (var tag in item.Tags)
        {
            if (s_tagToAgent.TryGetValue(tag, out var agentType))
                return agentType;
        }
        return null;
    }

    /// <summary>
    /// Sweep items stuck in InProgress whose relevant agents have all completed their runs
    /// AND are not currently running. For multi-agent Task items, force-register completion
    /// via the task tracker. For single-agent items, mark them Completed directly.
    /// </summary>
    private void SweepStuckInProgressItems(
        AgentContext context,
        ConcurrentDictionary<AgentType, bool> completedAgents,
        ConcurrentDictionary<AgentType, Task> runningAgents)
    {
        var swept = 0;
        foreach (var item in context.ExpandedRequirements)
        {
            if (item.Status != WorkItemStatus.InProgress) continue;
            if (!IsActionableWorkItem(item)) continue;

            if (item.ItemType == WorkItemType.Task)
            {
                var relevantAgents = GetRelevantTaskAgents(item);
                // Check if every relevant agent has finished at least one run AND is not currently running
                var allAgentsDone = relevantAgents.All(agentName =>
                    Enum.TryParse<AgentType>(agentName, ignoreCase: true, out var at) &&
                    completedAgents.ContainsKey(at) &&
                    !runningAgents.ContainsKey(at));

                if (!allAgentsDone) continue;

                // Force-complete via the tracker for each relevant agent
                foreach (var agentName in relevantAgents)
                    _taskTracker.MarkDone(item.Id, agentName);

                if (_taskTracker.AllDone(item.Id, relevantAgents))
                {
                    item.Status = WorkItemStatus.Completed;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                    swept++;
                }
            }
            else
            {
                // Bug / UseCase — single agent
                if (string.IsNullOrEmpty(item.AssignedAgent)) continue;
                if (!Enum.TryParse<AgentType>(item.AssignedAgent, ignoreCase: true, out var assignedType)) continue;
                // Skip if agent is currently running (it may still be processing this item)
                if (runningAgents.ContainsKey(assignedType)) continue;
                if (!completedAgents.ContainsKey(assignedType)) continue;

                item.Status = WorkItemStatus.Completed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                swept++;
            }
        }

        if (swept > 0)
            _logger.LogInformation("[Daemon] Swept {Count} stuck InProgress item(s) — force-completed", swept);
    }

    /// <summary>Get items this agent would claim (read-only peek for dispatch logging).</summary>
    private static List<ExpandedRequirement> GetClaimableItems(AgentContext context, AgentType agentType)
    {
        var result = new List<ExpandedRequirement>();
        var snapshot = context.ExpandedRequirements.Snapshot();
        foreach (var item in snapshot)
        {
            if (result.Count >= MaxClaimBatchSize) break;

            if (item.Status != WorkItemStatus.InQueue) continue;
            if (MatchesAgent(item, agentType))
                result.Add(item);
        }
        // Higher priority (lower number) first so orchestrator picks most important items first
        result.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return result;
    }

    /// <summary>Check if a backlog item matches an agent type.</summary>
    private static bool MatchesAgent(ExpandedRequirement item, AgentType agentType)
    {
        if (item.ItemType == WorkItemType.Task)
            return GetRelevantTaskAgents(item).Contains(agentType.ToString());
        if (item.ItemType == WorkItemType.Bug)
            return agentType == AgentType.BugFix;
        // Epics, UserStories, and UseCases are not directly claimable — they roll up from child tasks
        return false;
    }

    private static bool IsActionableWorkItem(ExpandedRequirement item) =>
        item.ItemType is WorkItemType.Task or WorkItemType.Bug;

    private static bool IsActionableBacklogCompleted(AgentContext context)
    {
        var actionable = context.ExpandedRequirements.Where(IsActionableWorkItem).ToList();
        if (actionable.Count == 0)
            return false;

        return actionable.All(i => i.Status == WorkItemStatus.Completed);
    }

    private static void RollupParentItems(IList<ExpandedRequirement> allItems)
    {
        var byParent = allItems
            .Where(c => !string.IsNullOrWhiteSpace(c.ParentId))
            .GroupBy(c => c.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var parent in allItems.Where(e => e.ItemType is WorkItemType.Epic or WorkItemType.UserStory))
        {
            if (!byParent.TryGetValue(parent.Id, out var children) || children.Count == 0)
                continue;

            if (children.All(c => c.Status == WorkItemStatus.Completed))
            {
                parent.Status = WorkItemStatus.Completed;
                parent.CompletedAt ??= DateTimeOffset.UtcNow;
                parent.AssignedAgent = string.Empty;
                continue;
            }

            parent.Status = WorkItemStatus.InProgress;
            parent.CompletedAt = null;
            parent.AssignedAgent = string.Empty;
        }
    }

    /// <summary>
    /// Determine which agents are responsible for a task by analyzing:
    ///   1. Explicit tags on the item
    ///   2. Title, Description, TechnicalNotes, DetailedSpec, Module text
    ///   3. Definition of Done checklist items (e.g. "unit tests written" → Testing)
    ///   4. Acceptance Criteria text
    ///   5. AffectedServices list
    /// Returns the sorted list of agent names relevant to this item.
    /// </summary>
    private static List<string> GetRelevantTaskAgents(ExpandedRequirement item)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── 1. Explicit tags — these are authoritative ──
        foreach (var tag in item.Tags)
        {
            if (s_tagToAgent.TryGetValue(tag, out var mapped))
                result.Add(mapped.ToString());
        }

        // For tasks whose tags map to a SINGLE primary agent (e.g. "database" → Database,
        // "contract" → Application), return ONLY that agent. This prevents text-matching
        // from dragging in every agent and blocking completion via the task tracker.
        // Multi-agent ownership is only needed when tags themselves map to multiple agents.
        if (result.Count > 0 && item.Tags.Count > 0)
        {
            // If all tags resolve to the same agent, that's the sole owner
            var distinctAgents = item.Tags
                .Where(t => s_tagToAgent.ContainsKey(t))
                .Select(t => s_tagToAgent[t].ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctAgents.Count == 1)
                return distinctAgents;
        }

        // ── 2. Combined text from task description + technical notes ──
        var text = string.Join(' ',
            new[]
            {
                item.Title,
                item.Description,
                item.TechnicalNotes,
                item.DetailedSpec,
                item.Module
            }.Where(s => !string.IsNullOrWhiteSpace(s)))
            .ToLowerInvariant();

        MapTextToAgents(text, result);

        // ── 3. Definition of Done items — each DOD entry can imply an agent ──
        foreach (var dod in item.DefinitionOfDone)
        {
            MapTextToAgents(dod.ToLowerInvariant(), result);
        }

        // ── 4. Acceptance Criteria text ──
        foreach (var ac in item.AcceptanceCriteria)
        {
            MapTextToAgents(ac.ToLowerInvariant(), result);
        }

        // ── 5. AffectedServices — if services are listed, code-gen agents are needed ──
        if (item.AffectedServices.Count > 0)
        {
            result.Add(AgentType.Database.ToString());
            result.Add(AgentType.ServiceLayer.ToString());
            result.Add(AgentType.Application.ToString());
        }

        if (item.ItemType == WorkItemType.Bug)
            result.Add(AgentType.BugFix.ToString());

        // ── Fallback: parse title alone when no agents matched yet ──
        if (result.Count == 0)
        {
            var title = (item.Title ?? string.Empty).ToLowerInvariant();
            MapTextToAgents(title, result);
        }

        // ── Ultimate fallback ──
        if (result.Count == 0)
            result.Add(AgentType.ServiceLayer.ToString());

        return result.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Keyword-to-agent mapping rules applied to any text corpus (title, description, DOD, AC).
    /// </summary>
    private static void MapTextToAgents(string text, HashSet<string> agents)
    {
        // Database layer
        if (text.Contains("database") || text.Contains("schema") || text.Contains("migration") ||
            text.Contains("sql") || text.Contains("dbcontext") || text.Contains("entity") ||
            text.Contains("table") || text.Contains("column") || text.Contains("index") ||
            text.Contains("rls") || text.Contains("row-level security") || text.Contains("db "))
            agents.Add(AgentType.Database.ToString());

        // Service layer
        if (text.Contains("service") || text.Contains("validation") || text.Contains("business logic") ||
            text.Contains("workflow") || text.Contains("dto") || text.Contains("repository") ||
            text.Contains("crud") || text.Contains("domain logic"))
            agents.Add(AgentType.ServiceLayer.ToString());

        // Application / API layer
        if (text.Contains("api") || text.Contains("endpoint") || text.Contains("controller") ||
            text.Contains("route") || text.Contains("gateway") || text.Contains("minimal api") ||
            text.Contains("rest") || text.Contains("http") || text.Contains("swagger"))
            agents.Add(AgentType.Application.ToString());

        // Integration
        if (text.Contains("integration") || text.Contains("adapter") || text.Contains("interoperability") ||
            text.Contains("kafka") || text.Contains("message") || text.Contains("event bus") ||
            text.Contains("outbox") || text.Contains("dead letter") || text.Contains("interop"))
            agents.Add(AgentType.Integration.ToString());

        // Testing
        if (text.Contains("test") || text.Contains("unit test") || text.Contains("assert") ||
            text.Contains("xunit") || text.Contains("moq") || text.Contains("coverage") ||
            text.Contains("test case") || text.Contains("verified by test"))
            agents.Add(AgentType.Testing.ToString());

        // Security
        if (text.Contains("security") || text.Contains("authentication") || text.Contains("authorization") ||
            text.Contains("rbac") || text.Contains("jwt") || text.Contains("token") ||
            text.Contains("encrypt") || text.Contains("audit trail"))
            agents.Add(AgentType.Security.ToString());

        // Access control
        if (text.Contains("access control") || text.Contains("permission") || text.Contains("role-based") ||
            text.Contains("tenant isolation") || text.Contains("multi-tenant"))
            agents.Add(AgentType.AccessControl.ToString());

        // Compliance
        if (text.Contains("hipaa") || text.Contains("phi") || text.Contains("protected health"))
            agents.Add(AgentType.HipaaCompliance.ToString());
        if (text.Contains("soc2") || text.Contains("soc 2") || text.Contains("compliance"))
            agents.Add(AgentType.Soc2Compliance.ToString());

        // Observability
        if (text.Contains("observability") || text.Contains("logging") || text.Contains("tracing") ||
            text.Contains("metrics") || text.Contains("health check") || text.Contains("telemetry") ||
            text.Contains("opentelemetry") || text.Contains("prometheus") || text.Contains("grafana"))
            agents.Add(AgentType.Observability.ToString());

        // Performance
        if (text.Contains("performance") || text.Contains("benchmark") || text.Contains("latency") ||
            text.Contains("throughput") || text.Contains("cache") || text.Contains("optimize"))
            agents.Add(AgentType.Performance.ToString());

        // Infrastructure / deployment
        if (text.Contains("infrastructure") || text.Contains("docker") || text.Contains("kubernetes") ||
            text.Contains("terraform") || text.Contains("helm") || text.Contains("k8s"))
            agents.Add(AgentType.Infrastructure.ToString());
        if (text.Contains("deploy") || text.Contains("ci/cd") || text.Contains("pipeline") ||
            text.Contains("github action"))
            agents.Add(AgentType.Deploy.ToString());

        // Documentation
        if (text.Contains("documentation") || text.Contains("swagger") || text.Contains("openapi") ||
            text.Contains("readme") || text.Contains("api doc"))
            agents.Add(AgentType.ApiDocumentation.ToString());

        // UI/UX
        if (text.Contains("ui") || text.Contains("ux") || text.Contains("frontend") ||
            text.Contains("blazor") || text.Contains("razor") || text.Contains("component") ||
            text.Contains("page") || text.Contains("layout") || text.Contains("dashboard"))
            agents.Add(AgentType.UiUx.ToString());

        // Configuration
        if (text.Contains("configuration") || text.Contains("appsettings") || text.Contains("environment variable") ||
            text.Contains("config"))
            agents.Add(AgentType.Configuration.ToString());

        // Migration
        if (text.Contains("data migration") || text.Contains("migrate"))
            agents.Add(AgentType.Migration.ToString());

        // Code quality
        if (text.Contains("code quality") || text.Contains("lint") || text.Contains("analyzer") ||
            text.Contains("refactor") || text.Contains("code review"))
            agents.Add(AgentType.CodeQuality.ToString());
    }

    private void ApplyAdaptiveWipLimits(AgentContext context)
    {
        var baseQueue = context.PipelineConfig?.MaxQueueItems > 0 ? context.PipelineConfig.MaxQueueItems : 10;
        var baseInDev = context.PipelineConfig?.MaxInDevItems > 0 ? context.PipelineConfig.MaxInDevItems : 10;

        if (context.ExpandedRequirements.Count == 0)
        {
            _lifecycle.MaxQueueItems = baseQueue;
            _lifecycle.MaxInDevItems = baseInDev;
            return;
        }

        var total = context.ExpandedRequirements.Count;
        var blocked = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Blocked);
        var ready = context.ExpandedRequirements.Count(e => e.Status is WorkItemStatus.New or WorkItemStatus.InQueue);

        var blockedRatio = (double)blocked / total;
        var queueCap = baseQueue;
        var inDevCap = baseInDev;

        if (blockedRatio >= 0.75)
        {
            queueCap = Math.Min(MaxAdaptiveWipCap, baseQueue + 10);
            inDevCap = Math.Min(MaxAdaptiveWipCap, baseInDev + 10);
        }
        else if (blockedRatio >= 0.55)
        {
            queueCap = Math.Min(MaxAdaptiveWipCap, baseQueue + 5);
            inDevCap = Math.Min(MaxAdaptiveWipCap, baseInDev + 5);
        }

        // Avoid inflating WIP when there is no backlog pressure.
        if (ready <= baseInDev)
        {
            queueCap = baseQueue;
            inDevCap = baseInDev;
        }

        if (_lifecycle.MaxQueueItems != queueCap || _lifecycle.MaxInDevItems != inDevCap)
        {
            _logger.LogInformation(
                "[Daemon] Adaptive WIP updated: queue {PrevQueue}->{QueueCap}, inDev {PrevDev}->{InDevCap} (blocked={Blocked}/{Total}, ratio={Ratio:P0}, ready={Ready})",
                _lifecycle.MaxQueueItems,
                queueCap,
                _lifecycle.MaxInDevItems,
                inDevCap,
                blocked,
                total,
                blockedRatio,
                ready);
        }

        _lifecycle.MaxQueueItems = queueCap;
        _lifecycle.MaxInDevItems = inDevCap;
    }

}
