using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform;

/// <summary>
/// Persisted record of a single inter-agent communication event.
/// Populated when <c>EnableAgentCommunicationLogging</c> is true in PipelineConfig.
/// Used for runtime analysis and agent self-improvement.
/// </summary>
public class AgentCommunicationLog : PlatformEntityBase
{
    [Required] public string RunId { get; set; } = null!;
    public string? ProjectId { get; set; }

    [Required] public string CommType { get; set; } = null!;
    [Required] public string FromAgent { get; set; } = null!;
    public string? ToAgent { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string? Category { get; set; }

    public DateTimeOffset EventTimestamp { get; set; } = DateTimeOffset.UtcNow;
}
