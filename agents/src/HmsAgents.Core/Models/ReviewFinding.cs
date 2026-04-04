using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class ReviewFinding
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ArtifactId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int? LineNumber { get; init; }
    public ReviewSeverity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
    public string? TracedRequirementId { get; init; }
}
