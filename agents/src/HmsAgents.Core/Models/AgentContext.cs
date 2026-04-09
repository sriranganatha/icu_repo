using System.Collections.Concurrent;
using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class AgentContext
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string RequirementsBasePath { get; init; } = string.Empty;
    public string OutputBasePath { get; init; } = string.Empty;
    public List<Requirement> Requirements { get; set; } = [];
    public SynchronizedList<ExpandedRequirement> ExpandedRequirements { get; set; } = new();
    public ConcurrentBag<CodeArtifact> Artifacts { get; set; } = [];
    public ConcurrentBag<ReviewFinding> Findings { get; set; } = [];
    public ConcurrentBag<AgentMessage> Messages { get; set; } = [];
    public ConcurrentBag<TestDiagnostic> TestDiagnostics { get; set; } = [];
    public ConcurrentDictionary<AgentType, AgentStatus> AgentStatuses { get; set; } = new();
    public ConcurrentDictionary<AgentType, int> RetryAttempts { get; set; } = new();
    public ConcurrentBag<AgentFailureRecord> FailureRecords { get; set; } = [];
    public PipelineConfig? PipelineConfig { get; set; }
    public ParsedDomainModel? DomainModel { get; set; }
    public int ReviewIteration { get; set; }
    /// <summary>Live instruction queue — new directives can be pushed mid-pipeline.</summary>
    public List<string> OrchestratorInstructions { get; set; } = [];
    /// <summary>Inter-agent message queue — agents post directives for other agents.</summary>
    public ConcurrentQueue<AgentDirective> DirectiveQueue { get; } = new();
    /// <summary>Development iteration counter — incremented each time the Review→BugFix loop completes.</summary>
    public int DevIteration { get; set; }
    /// <summary>Progress callback — agents call this to emit real-time running commentary to the dashboard. Pass (AgentType, message).</summary>
    public Func<AgentType, string, Task>? ReportProgress { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Set to true after the first DDL approval in this run — prevents re-prompting on agent re-dispatch.</summary>
    public bool DdlApprovedForRun { get; set; }
    /// <summary>Structured implementation plan created by PlanningAgent — guides downstream code-gen agents.</summary>
    public ImplementationPlan? ImplementationPlan { get; set; }

    // ── Agent-owned lifecycle ─────────────────────────────────
    // The orchestrator claims & starts items, then sets these before calling ExecuteAsync.
    // Agents call CompleteWorkItem / FailWorkItem for each item they process.

    /// <summary>Work items claimed for the current agent execution. Set by the orchestrator before calling ExecuteAsync.</summary>
    public List<ExpandedRequirement> CurrentClaimedItems { get; set; } = [];

    /// <summary>Marks a single work item as completed. Agents call this after successfully generating artifacts for an item.</summary>
    public Action<ExpandedRequirement>? CompleteWorkItem { get; set; }

    /// <summary>Marks a single work item as failed with a reason. Agents call this when they cannot process an item.</summary>
    public Action<ExpandedRequirement, string>? FailWorkItem { get; set; }

    // ── Requirement version history ──
    public ConcurrentBag<RequirementVersion> RequirementVersions { get; set; } = [];

    // ── BRD documents ──
    public ConcurrentBag<BrdDocument> BrdDocuments { get; set; } = [];

    // ── Pipeline checkpoints for replay/resume ──
    public ConcurrentBag<PipelineCheckpoint> Checkpoints { get; set; } = [];

    // ── Artifact conflicts ──
    public ConcurrentBag<ArtifactConflict> ArtifactConflicts { get; set; } = [];

    // ── Agent escalation policies ──
    public List<AgentEscalationPolicy> EscalationPolicies { get; set; } = [];

    // ── Release evidence ──
    public ReleaseEvidence? ReleaseEvidence { get; set; }

    // ── Sprint plans ──
    public List<SprintPlan> SprintPlans { get; set; } = [];

    // ── Learning records ──
    public ConcurrentBag<AgentLearningRecord> LearningRecords { get; set; } = [];
}
