using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class CiCdTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Provider { get; set; } = null!; // github_actions | gitlab_ci | azure_devops | jenkins
    public string? LanguageId { get; set; }
    [Required] public string PipelineYaml { get; set; } = string.Empty;

    public Language? Language { get; set; }
}
