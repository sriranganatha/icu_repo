namespace GNex.Core.Models;

public sealed class Requirement
{
    public string Id { get; init; } = string.Empty;
    /// <summary>The project this requirement belongs to. Null for legacy/global runs.</summary>
    public string? ProjectId { get; set; }
    public string SourceFile { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public int HeadingLevel { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public List<string> AcceptanceCriteria { get; init; } = [];
    public List<string> DependsOn { get; init; } = [];
}
