using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// A code quality metric recorded by review/build/quality agents.
/// Downstream agents consult these to avoid repeating flagged patterns.
/// </summary>
public sealed class QualityMetric
{
    /// <summary>The agent that recorded this metric.</summary>
    public AgentType Source { get; set; }

    /// <summary>Category of the metric (e.g. "Security", "Performance", "CodeSmell", "Coverage").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>What was measured or flagged.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Numeric score or count (e.g. test coverage %, number of violations).</summary>
    public double Value { get; set; }

    /// <summary>Target/threshold for this metric (e.g. 80 for 80% coverage).</summary>
    public double? Target { get; set; }

    /// <summary>Whether the metric meets its target.</summary>
    public bool Passed => Target is null || Value >= Target;

    /// <summary>The artifact or file this metric applies to (null = project-wide).</summary>
    public string? ArtifactPath { get; set; }

    /// <summary>When this metric was recorded.</summary>
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
