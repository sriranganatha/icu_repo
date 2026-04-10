using GNex.Core.Enums;

namespace GNex.Core.Interfaces;

/// <summary>
/// Verifiable audit logger for pipeline agents. Every agent action is recorded
/// with a hash chain for tamper detection — each entry's hash includes the
/// previous entry's hash, creating a blockchain-like audit trail.
/// </summary>
public interface IAuditLogger
{
    /// <summary>Log a structured audit entry for an agent action.</summary>
    Task LogAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>Convenience: log with inline params.</summary>
    Task LogAsync(AgentType agent, string runId, AuditAction action, string description,
        string? details = null, string? inputHash = null, string? outputHash = null,
        AuditSeverity severity = AuditSeverity.Info, CancellationToken ct = default);

    /// <summary>Verify the full audit chain integrity for a run. Returns (valid, brokenAtSequence).</summary>
    Task<(bool IsValid, int? BrokenAtSequence)> VerifyChainAsync(string runId, CancellationToken ct = default);
}

public enum AuditAction
{
    AgentStarted,
    AgentCompleted,
    AgentFailed,
    AgentRetried,
    DecisionMade,
    ArtifactGenerated,
    ArtifactModified,
    FindingRaised,
    RequirementExpanded,
    BacklogUpdated,
    HumanApprovalRequested,
    HumanApproved,
    HumanRejected,
    HumanOverride,
    RemediationAttempted,
    RemediationSucceeded,
    ConfigChanged,
    PipelineStarted,
    PipelineCompleted,
    SecurityCheck,
    ComplianceCheck
}

public enum AuditSeverity
{
    Info,
    Decision,
    Warning,
    Critical,
    SecurityEvent
}

public sealed class AuditEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public AgentType Agent { get; init; }
    public AuditAction Action { get; init; }
    public AuditSeverity Severity { get; init; } = AuditSeverity.Info;
    public string Description { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? InputHash { get; init; }
    public string? OutputHash { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Hash chain fields — set by the logger implementation
    public int Sequence { get; set; }
    public string PreviousHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;
}
