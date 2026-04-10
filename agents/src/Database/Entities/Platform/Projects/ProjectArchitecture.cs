using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class ProjectArchitecture : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string PatternId { get; set; } = null!; // FK to ArchitectureTemplate.Id
    public string? CustomizationsJson { get; set; }

    public Project? Project { get; set; }
}
