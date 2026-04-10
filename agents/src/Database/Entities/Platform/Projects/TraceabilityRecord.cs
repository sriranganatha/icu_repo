using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class TraceabilityRecord : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    public string? RequirementId { get; set; }
    public string? StoryId { get; set; }
    public string? TaskId { get; set; }
    public string? ArtifactId { get; set; }
    public string? TestId { get; set; }
    [Required] public string LinkType { get; set; } = "derives"; // derives | implements | tests | validates

    public Project? Project { get; set; }
}
