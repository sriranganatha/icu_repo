namespace HmsAgents.Core.Models;

/// <summary>
/// Tracks a single revision of a requirement for version history and diff.
/// </summary>
public sealed class RequirementVersion
{
    public string RequirementId { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> AcceptanceCriteria { get; init; } = [];
    public List<string> Tags { get; init; } = [];
    public string Module { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string ChangedBy { get; init; } = "system";
    public string ChangeReason { get; init; } = string.Empty;
}

/// <summary>
/// Semantic diff between two requirement versions.
/// </summary>
public sealed class RequirementDiff
{
    public string RequirementId { get; init; } = string.Empty;
    public int FromVersion { get; init; }
    public int ToVersion { get; init; }
    public bool TitleChanged { get; init; }
    public bool DescriptionChanged { get; init; }
    public List<string> AddedCriteria { get; init; } = [];
    public List<string> RemovedCriteria { get; init; } = [];
    public List<string> AddedTags { get; init; } = [];
    public List<string> RemovedTags { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
