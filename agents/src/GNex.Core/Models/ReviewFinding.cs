using GNex.Core.Enums;

namespace GNex.Core.Models;

public sealed class ReviewFinding
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>The project this finding belongs to. Null for legacy/global runs.</summary>
    public string? ProjectId { get; set; }
    public string ArtifactId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int? LineNumber { get; init; }
    public ReviewSeverity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
    public string? TracedRequirementId { get; init; }
}
