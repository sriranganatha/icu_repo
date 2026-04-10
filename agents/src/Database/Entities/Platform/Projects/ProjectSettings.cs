using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class ProjectSettings : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    public string? GitRepoUrl { get; set; }
    [Required] public string DefaultBranch { get; set; } = "main";
    public string? ArtifactStoragePath { get; set; }
    public string? NotificationConfigJson { get; set; } // {"slack_webhook":"...","email":"..."}

    public Project? Project { get; set; }
}
