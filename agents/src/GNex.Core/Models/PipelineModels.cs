namespace GNex.Core.Models;

/// <summary>
/// Orchestrator checkpoint for deterministic replay and resume.
/// </summary>
public sealed class PipelineCheckpoint
{
    public string RunId { get; init; } = string.Empty;
    public int SequenceNumber { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CheckpointType Type { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Dictionary<string, string> State { get; init; } = new();
    public List<string> CompletedAgents { get; init; } = [];
    public List<string> PendingAgents { get; init; } = [];
}

public enum CheckpointType
{
    AgentStarted,
    AgentCompleted,
    AgentFailed,
    WaveCompleted,
    HumanGateReached,
    PipelineResumed
}

/// <summary>
/// Conflict detected when two agents produce overlapping artifacts.
/// </summary>
public sealed class ArtifactConflict
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; init; } = string.Empty;
    public string ProducerA { get; init; } = string.Empty;
    public string ProducerB { get; init; } = string.Empty;
    public string ContentA { get; init; } = string.Empty;
    public string ContentB { get; init; } = string.Empty;
    public string ResolvedContent { get; set; } = string.Empty;
    public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum ConflictResolution
{
    Unresolved,
    KeepFirst,
    KeepSecond,
    Merged,
    ManualRequired
}

/// <summary>
/// Policy for per-agent SLA, retry budget, and escalation.
/// </summary>
public sealed class AgentEscalationPolicy
{
    public string AgentType { get; init; } = string.Empty;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan EscalationAfter { get; init; } = TimeSpan.FromMinutes(10);
    public EscalationAction OnExceed { get; init; } = EscalationAction.FailAndNotify;
}

public enum EscalationAction
{
    Retry,
    FailAndNotify,
    HumanEscalation,
    Skip
}

/// <summary>
/// Release evidence package for compliance sign-off.
/// </summary>
public sealed class ReleaseEvidence
{
    public string RunId { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public int TotalRequirements { get; init; }
    public int CoveredRequirements { get; init; }
    public int TotalTests { get; init; }
    public int PassedTests { get; init; }
    public int TotalFindings { get; init; }
    public int CriticalFindings { get; init; }
    public List<string> SecurityScanResults { get; init; } = [];
    public List<string> ComplianceCheckResults { get; init; } = [];
    public List<TraceabilityEntry> TraceabilityMatrix { get; init; } = [];
    public bool IsReleasable => CriticalFindings == 0
                                && CoveredRequirements == TotalRequirements
                                && PassedTests == TotalTests;
}

/// <summary>
/// Links a requirement to its implementing artifacts and verifying tests.
/// </summary>
public sealed class TraceabilityEntry
{
    public string RequirementId { get; init; } = string.Empty;
    public string RequirementTitle { get; init; } = string.Empty;
    public List<string> ImplementingArtifacts { get; init; } = [];
    public List<string> VerifyingTests { get; init; } = [];
    public bool FullyCovered => ImplementingArtifacts.Count > 0 && VerifyingTests.Count > 0;
}

/// <summary>
/// Sprint planning suggestion based on capacity and dependencies.
/// </summary>
public sealed class SprintPlan
{
    public int SprintNumber { get; init; }
    public string SprintName { get; init; } = string.Empty;
    public int CapacityPoints { get; init; }
    public int AllocatedPoints { get; set; }
    public List<string> ItemIds { get; init; } = [];
    public List<string> BlockedBy { get; init; } = [];
    public double UtilizationPercent => CapacityPoints > 0 ? (double)AllocatedPoints / CapacityPoints * 100 : 0;
}

/// <summary>
/// Historical learning record for agent routing optimization.
/// </summary>
public sealed class AgentLearningRecord
{
    public string RunId { get; init; } = string.Empty;
    public string AgentType { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public TimeSpan Duration { get; init; }
    public int RetryCount { get; init; }
    public int ArtifactsProduced { get; init; }
    public int FindingsProduced { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Notes { get; init; } = string.Empty;
}
