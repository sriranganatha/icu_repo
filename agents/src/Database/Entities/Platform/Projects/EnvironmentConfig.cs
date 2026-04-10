using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class EnvironmentConfig : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string EnvName { get; set; } = "dev"; // dev | staging | prod
    [Required] public string VariablesJson { get; set; } = "{}"; // encrypted at rest
    public string? InfraConfigJson { get; set; }

    public Project? Project { get; set; }
}
