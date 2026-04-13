using GNex.Core.Enums;

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
/// Historical learning record for continuous improvement across pipeline runs.
/// Writers: Review, Supervisor, GapAnalysis, Monitor, BugFix — create learnings from findings/fixes.
/// Persistence: Orchestrator saves to DB at pipeline end, loads at pipeline start.
/// Consumers: PromptGeneratorAgent injects into system prompts, code-gen agents read avoidance patterns.
/// 
/// Three-tier scope model:
///   Project → seen only in this project
///   Domain  → promoted when seen in 2+ projects in the same domain
///   Global  → promoted when seen in 3+ projects or 2+ distinct domains
/// </summary>
public sealed class AgentLearningRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string AgentType { get; init; } = string.Empty;

    /// <summary>Scope: Project (default), Domain (cross-project within domain), Global (universal).</summary>
    public LearningScope Scope { get; set; } = LearningScope.Project;

    /// <summary>Category of the learning (e.g. "CodeQuality", "Security", "Performance", "Integration", "Testing").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>The specific problem that was detected.</summary>
    public string Problem { get; init; } = string.Empty;

    /// <summary>How the problem was resolved (or should be avoided in future).</summary>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>Impact level: Low, Medium, High, Critical.</summary>
    public string Impact { get; init; } = "Medium";

    /// <summary>Which target agents should read this learning to avoid the same issue.</summary>
    public List<string> TargetAgents { get; init; } = [];

    /// <summary>Compact actionable rule for injection into LLM prompts (1-2 sentences max).</summary>
    public string PromptRule { get; init; } = string.Empty;

    /// <summary>Domain this learning originated from (e.g. "Healthcare", "FinTech").</summary>
    public string Domain { get; init; } = string.Empty;

    // ── Cross-project tracking ──
    /// <summary>Distinct project IDs where this learning has been observed.</summary>
    public List<string> SeenInProjects { get; set; } = [];

    /// <summary>Distinct domains where this learning has been observed.</summary>
    public List<string> SeenInDomains { get; set; } = [];

    // ── Confidence & verification ──
    /// <summary>0.0–1.0 confidence score. Auto-calculated from recurrence, verification, and scope.</summary>
    public double Confidence { get; set; } = 0.5;

    /// <summary>Whether a subsequent clean pipeline run confirmed this learning resolved the issue.</summary>
    public bool IsVerified { get; set; }

    /// <summary>Whether this learning should be deprecated (e.g. the underlying platform fixed the root cause).</summary>
    public bool IsDeprecated { get; set; }

    // ── Execution metadata ──
    public string TaskType { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public TimeSpan Duration { get; init; }
    public int RetryCount { get; init; }
    public int ArtifactsProduced { get; init; }
    public int FindingsProduced { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// How many times this same learning has been triggered across runs.
    /// Higher recurrence = more important to address.
    /// </summary>
    public int Recurrence { get; set; } = 1;

    /// <summary>Recalculate confidence from available signals.</summary>
    public void RecalculateConfidence()
    {
        var score = 0.3; // base
        if (IsVerified) score += 0.3;
        score += Math.Min(0.2, Recurrence * 0.04);                // +0.04 per recurrence, max 0.2
        score += Math.Min(0.1, SeenInProjects.Count * 0.05);      // +0.05 per project, max 0.1
        score += Math.Min(0.1, SeenInDomains.Count * 0.05);       // +0.05 per domain, max 0.1
        Confidence = Math.Min(1.0, score);
    }
}
