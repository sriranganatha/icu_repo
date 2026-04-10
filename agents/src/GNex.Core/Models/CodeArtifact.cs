using GNex.Core.Enums;

namespace GNex.Core.Models;

public sealed class CodeArtifact
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>The project this artifact belongs to. Null for legacy/global runs.</summary>
    public string? ProjectId { get; set; }
    public ArtifactLayer Layer { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    public AgentType ProducedBy { get; init; }
    public List<string> TracedRequirementIds { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
