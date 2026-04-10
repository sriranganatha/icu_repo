using System.Collections.Concurrent;
using GNex.Core.Enums;

namespace GNex.Core.Models;

public sealed class AgentContext
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string RequirementsBasePath { get; init; } = string.Empty;
    public string OutputBasePath { get; init; } = string.Empty;

    // ── Project-scoped context (Phase 9 — multi-project support) ──

    /// <summary>The ID of the project this pipeline is running for. Null for legacy/global runs.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Resolved technology stack entries for the project (language, framework, DB, etc.).</summary>
    public List<ResolvedTechStackEntry> ResolvedTechStack { get; set; } = [];

    /// <summary>Per-agent configuration overrides for this project (LLM model, system prompt, constraints).</summary>
    public Dictionary<AgentType, ProjectAgentConfig> AgentConfigOverrides { get; set; } = [];

    /// <summary>The SDLC workflow ID loaded from the DB for this project.</summary>
    public string? WorkflowId { get; set; }

    /// <summary>Resolved workflow stages ordered by execution sequence.</summary>
    public List<ResolvedStage> ResolvedStages { get; set; } = [];
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

    // ── Project-scoping helper ──────────────────────────────────

    /// <summary>
    /// Stamps <see cref="ProjectId"/> onto every artifact, finding, diagnostic,
    /// requirement, and expanded requirement that does not already have a project scope set.
    /// Call this after each agent execution to guarantee all outputs are project-tagged.
    /// </summary>
    public void StampProjectScope()
    {
        if (string.IsNullOrWhiteSpace(ProjectId)) return;
        var pid = ProjectId;

        foreach (var a in Artifacts)
            a.ProjectId ??= pid;
        foreach (var f in Findings)
            f.ProjectId ??= pid;
        foreach (var d in TestDiagnostics)
            d.ProjectId ??= pid;
        foreach (var r in Requirements)
            r.ProjectId ??= pid;
        foreach (var er in ExpandedRequirements)
            er.ProjectId ??= pid;
    }
}
