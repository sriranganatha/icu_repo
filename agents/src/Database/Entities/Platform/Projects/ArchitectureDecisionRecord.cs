using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class ArchitectureDecisionRecord : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string Title { get; set; } = null!;
    [Required] public string Context { get; set; } = string.Empty;
    [Required] public string Decision { get; set; } = string.Empty;
    public string? Consequences { get; set; }
    [Required] public string AdrStatus { get; set; } = "proposed"; // proposed | accepted | deprecated | superseded
    public DateTimeOffset? DecidedAt { get; set; }

    public Project? Project { get; set; }
}
