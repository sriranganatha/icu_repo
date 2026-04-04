using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Agents.Orchestrator;

/// <summary>
/// Raised by the orchestrator so the web layer (SignalR hub) can push live updates to the dashboard.
/// </summary>
public sealed class PipelineEvent
{
    public string RunId { get; init; } = string.Empty;
    public AgentType Agent { get; init; }
    public AgentStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public int ArtifactCount { get; init; }
    public int FindingCount { get; init; }
    public double ElapsedMs { get; init; }
    public int RetryAttempt { get; init; }
    public List<TestDiagnosticDto>? TestDiagnostics { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class TestDiagnosticDto
{
    public string TestName { get; init; } = string.Empty;
    public string AgentUnderTest { get; init; } = string.Empty;
    public int Outcome { get; init; } // 0=Passed, 1=Failed, 2=Skipped, 3=Remediated
    public string Diagnostic { get; init; } = string.Empty;
    public string Remediation { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public double DurationMs { get; init; }
    public int AttemptNumber { get; init; }
}

public interface IPipelineEventSink
{
    Task OnEventAsync(PipelineEvent evt, CancellationToken ct = default);
}
