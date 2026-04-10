using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class ProjectDependency : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string PackageName { get; set; } = null!;
    public string? VersionConstraint { get; set; } // e.g. ">=8.0.0 <9.0.0"
    [Required] public string Scope { get; set; } = "runtime"; // runtime | dev | test
    public string? Reason { get; set; }

    public Project? Project { get; set; }
}
