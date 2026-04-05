using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class CodeArtifact
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ArtifactLayer Layer { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    public AgentType ProducedBy { get; init; }
    public List<string> TracedRequirementIds { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
