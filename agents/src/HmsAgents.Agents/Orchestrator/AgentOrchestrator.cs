using System.Collections.Concurrent;
using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

/// <summary>
/// Daemon-style orchestrator. Runs as a coordinator loop that dispatches
/// parallel waves of agents based on dependency readiness.
/// Supports mid-pipeline requirement injection, background Review, backlog tracking,
/// and inter-agent directive messaging.
/// </summary>
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IEnumerable<IAgent> _agents;
    private readonly IArtifactWriter _writer;
    private readonly IPipelineEventSink _eventSink;
    private readonly IAuditLogger _audit;
    private readonly IHumanGate _humanGate;
    private readonly ILogger<AgentOrchestrator> _logger;
    private AgentContext? _current;

    private const int MaxAgentRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DaemonPollInterval = TimeSpan.FromMilliseconds(500);

    // ─── Dependency graph: agent → set of agents it must wait for ───
    private static readonly Dictionary<AgentType, AgentType[]> s_dependencies = new()
    {
        [AgentType.RequirementsReader]    = [],
        [AgentType.Architect]             = [AgentType.RequirementsReader],
        [AgentType.PlatformBuilder]       = [AgentType.Architect],
        [AgentType.RequirementsExpander]  = [AgentType.RequirementsReader],
        [AgentType.Backlog]               = [AgentType.RequirementsExpander],
        [AgentType.Database]              = [AgentType.PlatformBuilder],
        [AgentType.ServiceLayer]          = [AgentType.PlatformBuilder],
        [AgentType.Application]           = [AgentType.PlatformBuilder],
        [AgentType.Integration]           = [AgentType.PlatformBuilder],
        [AgentType.Testing]               = [AgentType.Database, AgentType.ServiceLayer, AgentType.PlatformBuilder],
        [AgentType.Review]               = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application, AgentType.Integration, AgentType.Testing],
        // Enrichment — only need Requirements
        [AgentType.Security]             = [AgentType.RequirementsReader],
        [AgentType.HipaaCompliance]      = [AgentType.RequirementsReader],
        [AgentType.Soc2Compliance]       = [AgentType.RequirementsReader],
        [AgentType.AccessControl]        = [AgentType.RequirementsReader],
        [AgentType.Observability]        = [AgentType.RequirementsReader],
        [AgentType.Infrastructure]       = [AgentType.RequirementsReader],
        [AgentType.ApiDocumentation]     = [AgentType.RequirementsReader],
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
        [AgentType.BugFix, AgentType.Performance, AgentType.Review, AgentType.Supervisor, AgentType.Backlog, AgentType.RequirementsExpander, AgentType.Deploy, AgentType.Build, AgentType.Monitor];

    private const int MaxBuildFixCycles = 3; // Max build→fix→rebuild iterations

    // Queue for mid-pipeline requirement injection
    private readonly ConcurrentQueue<List<Requirement>> _pendingRequirements = new();
    // Task completion fan-out tracker: taskId -> set(agentName)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _taskCompletedBy = new(StringComparer.OrdinalIgnoreCase);

    public AgentOrchestrator(
        IEnumerable<IAgent> agents,
        IArtifactWriter writer,
        IPipelineEventSink eventSink,
        IAuditLogger audit,
        IHumanGate humanGate,
        ILogger<AgentOrchestrator> logger)
    {
        _agents = agents;
        _writer = writer;
        _eventSink = eventSink;
        _audit = audit;
        _humanGate = humanGate;
        _logger = logger;
    }

    public AgentContext? GetCurrentContext() => _current;

    public void ResetContext()
    {
        _current = null;
        _taskCompletedBy.Clear();
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
            PipelineConfig = config
        };
        _taskCompletedBy.Clear();
        _current = context;

        if (!string.IsNullOrWhiteSpace(config.OrchestratorInstructions))
            context.OrchestratorInstructions.Add(config.OrchestratorInstructions);

        foreach (var agentType in Enum.GetValues<AgentType>())
            context.AgentStatuses[agentType] = AgentStatus.Idle;

        // Wire progress callback once — agents pass their own AgentType to avoid cross-contamination
        context.ReportProgress = async (agentType, msg) =>
            await PublishEvent(context, agentType, AgentStatus.Running, msg, 0, ct: ct);

        _logger.LogInformation("Pipeline {RunId} starting (daemon parallel mode)", context.RunId);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
            "Pipeline starting — daemon parallel mode", ct: ct);

        // ── Audit: pipeline started
        await _audit.LogAsync(AgentType.Orchestrator, context.RunId, AuditAction.PipelineStarted,
            $"Pipeline {context.RunId} starting — {Enum.GetValues<AgentType>().Length} agents, daemon parallel mode",
            $"RequirementsPath: {config.RequirementsPath}, OutputPath: {config.OutputPath}",
            severity: AuditSeverity.Info, ct: ct);

        // Build the work queue: all agents we want to run
        var pendingAgents = new HashSet<AgentType>(s_dependencies.Keys);
        pendingAgents.Remove(AgentType.Supervisor);  // Supervisor runs at the very end
        var completedAgents = new ConcurrentDictionary<AgentType, bool>(); // true=success, false=skipped
        var runningAgents = new ConcurrentDictionary<AgentType, Task>();
        var remediationDispatched = false;

        // ── Daemon loop: poll for ready agents, dispatch in parallel ──
        while (pendingAgents.Count > 0 || runningAgents.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            // Process any mid-pipeline requirement injections
            await ProcessPendingRequirements(context, pendingAgents, completedAgents, ct);

            // Process inter-agent directives
            ProcessDirectives(context, pendingAgents, completedAgents);

            // Find agents whose dependencies are all satisfied
            var readyBatch = pendingAgents
                .Where(a => GetDependencies(a).All(dep => completedAgents.ContainsKey(dep)))
                .ToList();

            // Dispatch all ready agents in parallel
            foreach (var agentType in readyBatch)
            {
                pendingAgents.Remove(agentType);
                var agent = _agents.FirstOrDefault(a => a.Type == agentType);
                if (agent is null)
                {
                    completedAgents[agentType] = false;
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
                    var success = await RunAgentWithHealingAsync(context, agent, ct);
                    completedAgents[agentType] = success;
                    runningAgents.TryRemove(agentType, out _);
                }, ct);
                runningAgents[agentType] = task;
            }

            // After Review completes — dispatch remediation agents dynamically
            if (!remediationDispatched && completedAgents.ContainsKey(AgentType.Review))
            {
                remediationDispatched = true;
                var remediationTypes = new HashSet<AgentType>();
                foreach (var finding in context.Findings.ToList())
                {
                    if (s_findingDispatch.TryGetValue(finding.Category, out var agents))
                        foreach (var at in agents)
                            remediationTypes.Add(at);
                }

                foreach (var rt in remediationTypes)
                {
                    if (!completedAgents.ContainsKey(rt) && !pendingAgents.Contains(rt) && !runningAgents.ContainsKey(rt))
                    {
                        _logger.LogInformation("[Daemon] Remediation dispatch: {Agent}", rt);
                        pendingAgents.Add(rt);
                    }
                }

                // After Review + Remediation, re-run Backlog to update statuses
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
                await Task.WhenAny(
                    Task.WhenAny(runningAgents.Values),
                    Task.Delay(DaemonPollInterval, ct));
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
                    await Task.Delay(DaemonPollInterval, ct);
                }
            }
        }

        // ── Background Review pass: re-validate all artifacts ──────
        var reviewAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Review);
        if (reviewAgent is not null)
        {
            _logger.LogInformation("[Daemon] Running final background Review pass on all artifacts");
            context.ReviewIteration++;
            await RunAgentWithHealingAsync(context, reviewAgent, ct);

            // Final Backlog update after review
            var backlog = _agents.FirstOrDefault(a => a.Type == AgentType.Backlog);
            if (backlog is not null)
                await RunAgentWithHealingAsync(context, backlog, ct);
        }

        // ── Build → Fix → Rebuild automated loop ───────────────────
        var buildAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Build);
        if (buildAgent is not null)
        {
            await RunBuildFixCycleAsync(context, buildAgent, ct);
        }

        // ── Deploy → Monitor → Fix feedback loop ───────────────────
        var deployAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Deploy);
        if (deployAgent is not null)
        {
            await RunAgentWithHealingAsync(context, deployAgent, ct);

            var monitorAgent = _agents.FirstOrDefault(a => a.Type == AgentType.Monitor);
            if (monitorAgent is not null)
            {
                await RunMonitorFixCycleAsync(context, monitorAgent, deployAgent, ct);
            }
        }

        // ── Final: Supervisor report ────────────────────────────────
        var supervisor = _agents.FirstOrDefault(a => a.Type == AgentType.Supervisor);
        if (supervisor is not null)
        {
            _logger.LogInformation("[Daemon] All agents done — running Supervisor final report");
            await RunAgentWithHealingAsync(context, supervisor, ct);
        }

        // Write artifacts to disk
        if (context.Artifacts.Count > 0 && !string.IsNullOrEmpty(config.OutputPath))
        {
            await _writer.WriteAllAsync(context.Artifacts, config.OutputPath, ct);
            _logger.LogInformation("Wrote {Count} artifacts to {Path}", context.Artifacts.Count, config.OutputPath);
        }

        context.CompletedAt = DateTimeOffset.UtcNow;
        context.AgentStatuses[AgentType.Orchestrator] = AgentStatus.Completed;
        _logger.LogInformation(
            "Pipeline {RunId} completed — {Artifacts} artifacts, {Findings} findings, {Tests} diagnostics, {Backlog} backlog items",
            context.RunId, context.Artifacts.Count, context.Findings.Count, context.TestDiagnostics.Count, context.ExpandedRequirements.Count);

        await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Completed,
            $"Pipeline complete — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings, {context.ExpandedRequirements.Count} backlog items",
            artifactCount: context.Artifacts.Count, findingCount: context.Findings.Count, ct: ct);

        // ── Audit: pipeline completed
        await _audit.LogAsync(AgentType.Orchestrator, context.RunId, AuditAction.PipelineCompleted,
            $"Pipeline {context.RunId} completed — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings",
            $"Backlog: {context.ExpandedRequirements.Count}, TestDiagnostics: {context.TestDiagnostics.Count}, " +
            $"Duration: {(context.CompletedAt!.Value - DateTimeOffset.Parse(context.RunId.Split('-').First())).TotalSeconds:F0}s",
            severity: AuditSeverity.Info, ct: ct);

        // ── Post-pipeline instructions ──────────────────────────────
        await ExecutePostPipelineInstructions(context, config, ct);

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
            OutputBasePath = config.OutputPath
        };
        _current = context;

        var agent = _agents.FirstOrDefault(a => a.Type == agentType)
            ?? throw new InvalidOperationException($"Agent {agentType} not registered.");

        await agent.ExecuteAsync(context, ct);
        return context;
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

            // ── HITL: gate for Database DDL execution (only prompt once per run)
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
                    context.DdlApprovedForRun = true; // don't prompt again on re-dispatch
                    _logger.LogInformation("[HITL] Human APPROVED DDL execution for run {RunId}", context.RunId);
                    await _audit.LogAsync(agent.Type, context.RunId, AuditAction.HumanApproved,
                        "DDL execution approved by human operator", severity: AuditSeverity.Decision, ct: ct);
                }
                else if (ddlDecision == HumanDecision.Rejected)
                {
                    _logger.LogWarning("[HITL] Human REJECTED DDL execution — skipping Database agent");
                    await _audit.LogAsync(agent.Type, context.RunId, AuditAction.HumanRejected,
                        "DDL execution rejected by human operator", severity: AuditSeverity.Decision, ct: ct);
                    context.PipelineConfig.ExecuteDdl = false; // disable DDL, let it generate artifacts only
                }
                else // TimedOut
                {
                    _logger.LogWarning("[HITL] DDL approval timed out — disabling DDL execution");
                    await _audit.LogAsync(agent.Type, context.RunId, AuditAction.HumanRejected,
                        "DDL approval timed out — DDL execution disabled for safety", severity: AuditSeverity.Warning, ct: ct);
                    context.PipelineConfig.ExecuteDdl = false;
                }
            }

            AgentResult result;
            try
            {
                // ── Assign matching backlog items to this agent ──
                await ClaimBacklogItems(context, agent.Type);

                result = await agent.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await PublishEvent(context, agent.Type, AgentStatus.Failed,
                    $"{agent.Name} cancelled", attempt, elapsed: sw.Elapsed.TotalMilliseconds, ct: ct);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Self-Heal] {Agent} crashed", agent.Name);
                result = new AgentResult
                {
                    Agent = agent.Type, Success = false,
                    Errors = [ex.Message],
                    Summary = $"{agent.Name} crashed: {ex.Message}",
                    Duration = sw.Elapsed
                };
                context.AgentStatuses[agent.Type] = AgentStatus.Failed;
            }

            await PublishEvent(context, agent.Type,
                result.Success ? AgentStatus.Completed : AgentStatus.Failed,
                result.Summary, attempt,
                result.Artifacts.Count, result.Findings.Count,
                sw.Elapsed.TotalMilliseconds, result.TestDiagnostics, ct);

            // ── Complete backlog items assigned to this agent ──
            if (result.Success)
                await CompleteBacklogItems(context, agent.Type);

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
            context.RetryAttempts[result.Agent] = context.RetryAttempts.GetValueOrDefault(result.Agent, 0) + 1;
            context.FailureRecords.Add(new AgentFailureRecord
            {
                FailedAgent = agent.Type,
                Attempt = attempt + 1,
                Error = lastError ?? "Unknown",
                Summary = result.Summary
            });

            // Heal cycle for non-heal agents
            if (!s_healCycleAgents.Contains(agent.Type))
            {
                await RunHealCycleAsync(context, agent.Type, lastError, attempt + 1, ct);
            }
        }

        // Exhausted retries — mark completed so downstream doesn't block
        _logger.LogWarning("[Self-Heal] {Agent} FAILED after {Max} attempts — skipping", agent.Name, MaxAgentRetries + 1);
        context.TestDiagnostics.Add(new TestDiagnostic
        {
            TestName = $"SelfHeal_{agent.Type}_Skipped",
            AgentUnderTest = agent.Type.ToString(),
            Outcome = TestOutcome.Failed,
            Diagnostic = $"Exhausted {MaxAgentRetries + 1} attempts: {lastError}",
            Remediation = "Agent skipped — pipeline continued without it",
            Category = "SelfHealing",
            DurationMs = 0,
            AttemptNumber = MaxAgentRetries + 1
        });
        context.AgentStatuses[agent.Type] = AgentStatus.Completed;
        return false;
    }

    // ─── Build → Fix → Rebuild Cycle ────────────────────────────────

    private async Task RunBuildFixCycleAsync(AgentContext context, IAgent buildAgent, CancellationToken ct)
    {
        for (var cycle = 0; cycle < MaxBuildFixCycles; cycle++)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("[Build-Fix] Cycle {Cycle}/{Max}", cycle + 1, MaxBuildFixCycles);

            await PublishEvent(context, AgentType.Build, AgentStatus.Running,
                $"Build cycle {cycle + 1}/{MaxBuildFixCycles} — compiling generated solution...", ct: ct);

            var buildResult = await buildAgent.ExecuteAsync(context, ct);

            await PublishEvent(context, AgentType.Build,
                buildResult.Success ? AgentStatus.Completed : AgentStatus.Failed,
                buildResult.Summary, ct: ct);

            if (buildResult.Success)
            {
                _logger.LogInformation("[Build-Fix] Build succeeded on cycle {Cycle}", cycle + 1);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Build passed on cycle {cycle + 1} — proceeding to deployment", ct: ct);

                await _audit.LogAsync(AgentType.Build, context.RunId, AuditAction.AgentCompleted,
                    $"Build succeeded on cycle {cycle + 1}/{MaxBuildFixCycles}",
                    $"Errors: 0, Findings: {buildResult.Findings.Count}",
                    severity: AuditSeverity.Info, ct: ct);
                return;
            }

            // Build failed — collect error findings and dispatch to BugFix
            var buildErrors = buildResult.Findings
                .Where(f => f.Category == "Build" && f.Severity >= ReviewSeverity.Error)
                .ToList();

            _logger.LogWarning("[Build-Fix] Build failed with {Count} errors — dispatching to BugFix", buildErrors.Count);
            await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                $"Build failed with {buildErrors.Count} errors — dispatching BugFix agent (cycle {cycle + 1})...", ct: ct);

            await _audit.LogAsync(AgentType.Build, context.RunId, AuditAction.AgentFailed,
                $"Build failed on cycle {cycle + 1}: {buildErrors.Count} errors",
                $"Errors: {string.Join("; ", buildErrors.Take(5).Select(e => Truncate(e.Message, 80)))}",
                severity: AuditSeverity.Warning, ct: ct);

            // Run BugFix to attempt code repairs
            var bugFix = _agents.FirstOrDefault(a => a.Type == AgentType.BugFix);
            if (bugFix is not null)
            {
                await PublishEvent(context, AgentType.BugFix, AgentStatus.Running,
                    $"BugFix repairing {buildErrors.Count} build errors (cycle {cycle + 1})...", ct: ct);
                await RunSingleHealAgent(context, bugFix,
                    $"BugFix repairing {buildErrors.Count} build errors from build cycle {cycle + 1}", ct);
            }
            else
            {
                _logger.LogWarning("[Build-Fix] BugFix agent not available — cannot auto-repair");
                break;
            }

            if (cycle + 1 >= MaxBuildFixCycles)
            {
                _logger.LogWarning("[Build-Fix] Exhausted {Max} build-fix cycles — build still failing", MaxBuildFixCycles);
                await PublishEvent(context, AgentType.Orchestrator, AgentStatus.Running,
                    $"Build-Fix cycle exhausted ({MaxBuildFixCycles} attempts) — build errors remain, proceeding to deployment anyway", ct: ct);
            }
        }
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

            var monitorResult = await monitorAgent.ExecuteAsync(context, ct);

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
            result = await agent.ExecuteAsync(context, ct);
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

    private static AgentType[] GetDependencies(AgentType agent) =>
        s_dependencies.TryGetValue(agent, out var deps) ? deps : [];

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

    // ── Backlog item <-> Agent mapping ──────────────────────────────

    /// <summary>Maps task tags → responsible agent types.</summary>
    private static readonly Dictionary<string, AgentType> s_tagToAgent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["database"]       = AgentType.Database,
        ["service"]        = AgentType.ServiceLayer,
        ["api"]            = AgentType.Application,
        ["application"]    = AgentType.Application,
        ["testing"]        = AgentType.Testing,
        ["integration"]    = AgentType.Integration,
        ["security"]       = AgentType.Security,
        ["hipaa"]          = AgentType.HipaaCompliance,
        ["compliance"]     = AgentType.Soc2Compliance,
        ["performance"]    = AgentType.Performance,
        ["accesscontrol"]  = AgentType.AccessControl,
        ["observability"]  = AgentType.Observability,
        ["infrastructure"] = AgentType.Infrastructure,
        ["documentation"]  = AgentType.ApiDocumentation,
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

    /// <summary>Get items this agent would claim (read-only peek for dispatch logging).</summary>
    private static List<ExpandedRequirement> GetClaimableItems(AgentContext context, AgentType agentType)
    {
        var result = new List<ExpandedRequirement>();
        var snapshot = context.ExpandedRequirements.ToList();
        foreach (var item in snapshot)
        {
            if (item.Status != WorkItemStatus.InQueue && item.Status != WorkItemStatus.New) continue;
            if (!string.IsNullOrEmpty(item.AssignedAgent)) continue;
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
        if (item.ItemType is WorkItemType.Epic or WorkItemType.UserStory && s_epicAgents.Contains(agentType))
            return true;
        if (item.ItemType == WorkItemType.UseCase && s_agentTags.ContainsKey(agentType))
            return true;
        return false;
    }

    private static List<string> GetRelevantTaskAgents(ExpandedRequirement item)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in item.Tags)
        {
            if (s_tagToAgent.TryGetValue(tag, out var mapped))
                result.Add(mapped.ToString());
        }

        // Fallback from title text when tags are weak/missing
        var title = item.Title ?? string.Empty;
        if (result.Count == 0)
        {
            if (title.Contains("database", StringComparison.OrdinalIgnoreCase) || title.Contains("migration", StringComparison.OrdinalIgnoreCase))
                result.Add(AgentType.Database.ToString());
            else if (title.Contains("test", StringComparison.OrdinalIgnoreCase))
                result.Add(AgentType.Testing.ToString());
            else if (title.Contains("integration", StringComparison.OrdinalIgnoreCase))
                result.Add(AgentType.Integration.ToString());
            else
                result.Add(AgentType.ServiceLayer.ToString());
        }

        return result.OrderBy(x => x).ToList();
    }

    /// <summary>Claim matching InQueue backlog items for this agent → set UnderDev + AssignedAgent.</summary>
    private async Task ClaimBacklogItems(AgentContext context, AgentType agentType)
    {
        var agentName = agentType.ToString();
        var claimed = new List<string>();

        var snapshot = context.ExpandedRequirements.ToList();
        foreach (var item in snapshot)
        {
            if (item.Status != WorkItemStatus.InQueue && item.Status != WorkItemStatus.New) continue;

            if (item.ItemType == WorkItemType.Task)
            {
                var relevantAgents = GetRelevantTaskAgents(item);
                if (!relevantAgents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
                    continue;

                item.AssignedAgent = string.Join(",", relevantAgents);
                item.Status = WorkItemStatus.UnderDev;
                item.StartedAt ??= DateTimeOffset.UtcNow;
                claimed.Add(item.Title);
                continue;
            }

            if (!string.IsNullOrEmpty(item.AssignedAgent)) continue;

            if (MatchesAgent(item, agentType))
            {
                item.AssignedAgent = agentName;
                item.Status = WorkItemStatus.UnderDev;
                item.StartedAt ??= DateTimeOffset.UtcNow;
                claimed.Add(item.Title);
            }
        }

        if (claimed.Count > 0)
        {
            _logger.LogInformation("[Backlog] {Agent} claimed {Count} backlog items", agentName, claimed.Count);
            await PublishEvent(context, agentType, AgentStatus.Running,
                $"Picked up {claimed.Count} backlog items: {string.Join(", ", claimed.Take(4).Select(t => Truncate(t, 45)))}{(claimed.Count > 4 ? $" (+{claimed.Count - 4} more)" : "")}",
                0, ct: CancellationToken.None);
        }
    }

    /// <summary>Mark all items assigned to this agent as Completed.</summary>
    private async Task CompleteBacklogItems(AgentContext context, AgentType agentType)
    {
        var agentName = agentType.ToString();
        var completedCount = 0;

        var snapshot = context.ExpandedRequirements.ToList();
        foreach (var item in snapshot)
        {
            if (item.Status == WorkItemStatus.Completed) continue;

            if (item.ItemType == WorkItemType.Task)
            {
                var relevantAgents = GetRelevantTaskAgents(item);
                if (!relevantAgents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
                    continue;

                var doneSet = _taskCompletedBy.GetOrAdd(item.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
                doneSet[agentName] = 1;

                var allDone = relevantAgents.All(a => doneSet.ContainsKey(a));
                if (allDone)
                {
                    item.Status = WorkItemStatus.Completed;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                    item.AssignedAgent = string.Join(",", relevantAgents);
                    completedCount++;
                }
                continue;
            }

            if (item.AssignedAgent != agentName) continue;

            item.Status = WorkItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            completedCount++;
        }

        // Also propagate: if all children of a parent are Completed, complete the parent
        foreach (var parent in context.ExpandedRequirements
            .Where(e => e.ItemType is WorkItemType.Epic or WorkItemType.UserStory))
        {
            if (parent.Status == WorkItemStatus.Completed) continue;
            var children = context.ExpandedRequirements.Where(c => c.ParentId == parent.Id).ToList();
            if (children.Count > 0 && children.All(c => c.Status == WorkItemStatus.Completed))
            {
                parent.Status = WorkItemStatus.Completed;
                parent.CompletedAt = DateTimeOffset.UtcNow;
                if (string.IsNullOrEmpty(parent.AssignedAgent))
                    parent.AssignedAgent = agentName;
                completedCount++;
            }
        }

        if (completedCount > 0)
        {
            _logger.LogInformation("[Backlog] {Agent} completed {Count} backlog items", agentName, completedCount);
            await PublishEvent(context, agentType, AgentStatus.Running,
                $"Completed {completedCount} backlog items",
                0, ct: CancellationToken.None);
        }
    }
}
