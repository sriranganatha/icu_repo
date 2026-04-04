using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class TestDiagnostic
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string TestName { get; init; } = string.Empty;
    public string AgentUnderTest { get; init; } = string.Empty;
    public TestOutcome Outcome { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
    public string Remediation { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public double DurationMs { get; init; }
    public int AttemptNumber { get; init; } = 1;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public enum TestOutcome
{
    Passed,
    Failed,
    Skipped,
    Remediated
}
