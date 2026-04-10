using GNex.Core.Enums;

namespace GNex.Core.Interfaces;

/// <summary>
/// Human-in-the-loop gate. Agents call this when they want human approval
/// before proceeding with a critical action. The orchestrator pauses the
/// agent until the human responds via the dashboard.
/// </summary>
public interface IHumanGate
{
    /// <summary>
    /// Request human approval. Blocks until the human responds or the timeout expires.
    /// Returns the decision (Approved, Rejected, TimedOut).
    /// </summary>
    Task<HumanDecision> RequestApprovalAsync(HumanApprovalRequest request, CancellationToken ct = default);

    /// <summary>Submit a human decision for a pending request.</summary>
    Task SubmitDecisionAsync(string requestId, bool approved, string? reason = null, CancellationToken ct = default);

    /// <summary>Get all pending approval requests.</summary>
    Task<List<HumanApprovalRequest>> GetPendingRequestsAsync(CancellationToken ct = default);

    /// <summary>Get decision history for a run.</summary>
    Task<List<HumanApprovalRequest>> GetDecisionHistoryAsync(string runId, CancellationToken ct = default);
}

public sealed class HumanApprovalRequest
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public AgentType RequestingAgent { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Details { get; init; }
    public HumanGateCategory Category { get; init; }
    public HumanDecision Decision { get; set; } = HumanDecision.Pending;
    public string? DecisionReason { get; set; }
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
}

public enum HumanDecision
{
    Pending,
    Approved,
    Rejected,
    TimedOut,
    AutoApproved
}

public enum HumanGateCategory
{
    DatabaseDdl,
    SecurityViolation,
    ComplianceViolation,
    CriticalFinding,
    DeploymentAction,
    ArtifactDeletion,
    ConfigurationChange,
    ExternalApiCall,
    DataMigration,
    AgentRemediation
}
