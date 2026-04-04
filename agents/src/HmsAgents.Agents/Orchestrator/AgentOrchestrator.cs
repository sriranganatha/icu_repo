using System.Collections.Concurrent;
using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IEnumerable<IAgent> _agents;
    private readonly IArtifactWriter _writer;
    private readonly IPipelineEventSink _eventSink;
    private readonly ILogger<AgentOrchestrator> _logger;
    private AgentContext? _current;

    private const int MaxStageRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    // Pipeline execution order — each group runs sequentially; within a group agents could be parallel.
    // Supervisor runs last to validate everything.
    private static readonly AgentType[][] s_pipeline =
    [
        [AgentType.RequirementsReader],
        [AgentType.Database],
        [AgentType.ServiceLayer],
        [AgentType.Application, AgentType.Integration],
        [AgentType.Testing],
        [AgentType.Review],
        [AgentType.Supervisor],
    ];

    public AgentOrchestrator(
        IEnumerable<IAgent> agents,
        IArtifactWriter writer,
        IPipelineEventSink eventSink,
        ILogger<AgentOrchestrator> logger)
    {
        _agents = agents;
        _writer = writer;
        _eventSink = eventSink;
        _logger = logger;
    }

    public AgentContext? GetCurrentContext() => _current;

    public async Task<AgentContext> RunPipelineAsync(PipelineConfig config, CancellationToken ct = default)
    {
        var context = new AgentContext
        {
            RequirementsBasePath = config.RequirementsPath,
            OutputBasePath = config.OutputPath,
            PipelineConfig = config
        };
        _current = context;

        foreach (var agentType in Enum.GetValues<AgentType>())
            context.AgentStatuses[agentType] = AgentStatus.Idle;

        _logger.LogInformation("Pipeline {RunId} starting", context.RunId);

        foreach (var stage in s_pipeline)
        {
            var stageAgents = stage
                .Select(t => _agents.FirstOrDefault(a => a.Type == t))
                .Where(a => a is not null)
                .ToList();

            if (stageAgents.Count == 0) continue;

            var stageSuccess = false;

            for (var attempt = 0; attempt <= MaxStageRetries; attempt++)
            {
                if (attempt > 0)
                {
                    _logger.LogWarning("Retrying stage [{Agents}] — attempt {Attempt}/{Max}",
                        string.Join(", ", stage), attempt, MaxStageRetries);
                    await Task.Delay(RetryDelay * attempt, ct);
                }

                // Run agents in this stage concurrently
                var tasks = stageAgents.Select(async agent =>
                {
                    var sw = Stopwatch.StartNew();
                    var retryAttempt = context.RetryAttempts.GetValueOrDefault(agent!.Type, 0) + (attempt > 0 ? 1 : 0);

                    await _eventSink.OnEventAsync(new PipelineEvent
                    {
                        RunId = context.RunId, Agent = agent.Type,
                        Status = AgentStatus.Running,
                        Message = attempt > 0
                            ? $"{agent.Name} retrying (attempt {attempt + 1})..."
                            : $"{agent.Name} starting...",
                        RetryAttempt = attempt
                    }, ct);

                    AgentResult result;
                    try
                    {
                        result = await agent.ExecuteAsync(context, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Agent {Agent} threw unhandled exception", agent.Name);
                        result = new AgentResult
                        {
                            Agent = agent.Type,
                            Success = false,
                            Errors = [ex.Message],
                            Summary = $"{agent.Name} crashed: {ex.Message}",
                            Duration = sw.Elapsed
                        };
                        context.AgentStatuses[agent.Type] = AgentStatus.Failed;
                    }

                    // Convert test diagnostics for the event
                    List<TestDiagnosticDto>? diagDtos = null;
                    if (result.TestDiagnostics.Count > 0)
                    {
                        diagDtos = result.TestDiagnostics.Select(d => new TestDiagnosticDto
                        {
                            TestName = d.TestName,
                            AgentUnderTest = d.AgentUnderTest,
                            Outcome = (int)d.Outcome,
                            Diagnostic = d.Diagnostic,
                            Remediation = d.Remediation,
                            Category = d.Category,
                            DurationMs = d.DurationMs,
                            AttemptNumber = d.AttemptNumber
                        }).ToList();
                    }

                    await _eventSink.OnEventAsync(new PipelineEvent
                    {
                        RunId = context.RunId, Agent = agent.Type,
                        Status = result.Success ? AgentStatus.Completed : AgentStatus.Failed,
                        Message = result.Summary,
                        ArtifactCount = result.Artifacts.Count,
                        FindingCount = result.Findings.Count,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds,
                        RetryAttempt = attempt,
                        TestDiagnostics = diagDtos
                    }, ct);

                    return result;
                });

                var results = await Task.WhenAll(tasks);

                if (results.All(r => r.Success))
                {
                    stageSuccess = true;
                    break;
                }

                // Track retry attempts for failed agents
                foreach (var r in results.Where(r => !r.Success))
                    context.RetryAttempts[r.Agent] = context.RetryAttempts.GetValueOrDefault(r.Agent, 0) + 1;

                // Don't retry the Supervisor stage — it reports findings, not failures
                if (stage.Contains(AgentType.Supervisor))
                {
                    stageSuccess = true; // Supervisor "failure" means it found issues, not that it broke
                    break;
                }
            }

            if (!stageSuccess)
            {
                _logger.LogWarning("Pipeline stopped — stage failure after {Max} retries", MaxStageRetries + 1);

                // Still run Supervisor to report diagnostics even on failure
                var supervisor = _agents.FirstOrDefault(a => a.Type == AgentType.Supervisor);
                if (supervisor is not null && !stage.Contains(AgentType.Supervisor))
                {
                    _logger.LogInformation("Running Supervisor to diagnose failure...");
                    var sw = Stopwatch.StartNew();
                    await _eventSink.OnEventAsync(new PipelineEvent
                    {
                        RunId = context.RunId, Agent = AgentType.Supervisor,
                        Status = AgentStatus.Running, Message = "Supervisor diagnosing pipeline failure..."
                    }, ct);

                    var supResult = await supervisor.ExecuteAsync(context, ct);

                    List<TestDiagnosticDto>? supDiagDtos = null;
                    if (supResult.TestDiagnostics.Count > 0)
                    {
                        supDiagDtos = supResult.TestDiagnostics.Select(d => new TestDiagnosticDto
                        {
                            TestName = d.TestName,
                            AgentUnderTest = d.AgentUnderTest,
                            Outcome = (int)d.Outcome,
                            Diagnostic = d.Diagnostic,
                            Remediation = d.Remediation,
                            Category = d.Category,
                            DurationMs = d.DurationMs,
                            AttemptNumber = d.AttemptNumber
                        }).ToList();
                    }

                    await _eventSink.OnEventAsync(new PipelineEvent
                    {
                        RunId = context.RunId, Agent = AgentType.Supervisor,
                        Status = supResult.Success ? AgentStatus.Completed : AgentStatus.Failed,
                        Message = supResult.Summary,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds,
                        TestDiagnostics = supDiagDtos
                    }, ct);
                }

                break;
            }
        }

        // Write all artifacts to disk
        if (context.Artifacts.Count > 0 && !string.IsNullOrEmpty(config.OutputPath))
        {
            await _writer.WriteAllAsync(context.Artifacts, config.OutputPath, ct);
            _logger.LogInformation("Wrote {Count} artifacts to {Path}", context.Artifacts.Count, config.OutputPath);
        }

        context.CompletedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Pipeline {RunId} completed — {Artifacts} artifacts, {Findings} findings, {Tests} test diagnostics",
            context.RunId, context.Artifacts.Count, context.Findings.Count, context.TestDiagnostics.Count);

        return context;
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
}
