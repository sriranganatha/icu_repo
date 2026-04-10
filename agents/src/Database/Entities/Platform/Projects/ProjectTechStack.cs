using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class ProjectTechStack : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string Layer { get; set; } = null!; // frontend | backend | database | cache | queue | search | infra
    [Required] public string TechnologyId { get; set; } = null!; // FK to Language, Framework, or DatabaseTechnology
    [Required] public string TechnologyType { get; set; } = null!; // language | framework | database | cloud | devops
    public string? Version { get; set; }
    public string? ConfigOverridesJson { get; set; }

    public Project? Project { get; set; }
}
