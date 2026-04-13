using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.AgentRegistry;

/// <summary>
/// Persistent learning record stored in DB across pipeline runs.
/// Supports three-tier scoping: Project → Domain → Global.
/// Learnings auto-promote when recurrence crosses thresholds.
/// </summary>
public class AgentLearning : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = string.Empty;
    [Required] public string RunId { get; set; } = string.Empty;
    [Required] public string AgentTypeCode { get; set; } = string.Empty;
    [Required] public string Category { get; set; } = string.Empty;
    [Required] public string Problem { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Impact { get; set; } = "Medium";

    /// <summary>Comma-separated list of target agent type codes that should read this learning.</summary>
    public string TargetAgents { get; set; } = string.Empty;

    /// <summary>Compact rule for LLM prompt injection (1-2 sentences).</summary>
    public string PromptRule { get; set; } = string.Empty;

    /// <summary>How many times this pattern has been seen across runs.</summary>
    public int Recurrence { get; set; } = 1;

    /// <summary>The domain that produced this learning (for cross-project reuse within same domain).</summary>
    public string Domain { get; set; } = string.Empty;

    // ── Scope + confidence ──────────────────────────────────────

    /// <summary>Project (0), Domain (1), Global (2). Auto-promoted by LearningAggregator.</summary>
    public int Scope { get; set; } = 0;

    /// <summary>0.0–1.0 confidence score computed from recurrence, verification, and spread.</summary>
    public double Confidence { get; set; } = 0.5;

    /// <summary>Whether a subsequent clean pipeline confirmed this learning works.</summary>
    public bool IsVerified { get; set; }

    /// <summary>Whether this learning is obsolete (platform fixed the root cause).</summary>
    public bool IsDeprecated { get; set; }

    // ── Cross-project tracking ──────────────────────────────────

    /// <summary>Comma-separated distinct project IDs where this learning was observed.</summary>
    public string SeenInProjects { get; set; } = string.Empty;

    /// <summary>Comma-separated distinct domains where this learning was observed.</summary>
    public string SeenInDomains { get; set; } = string.Empty;
}
