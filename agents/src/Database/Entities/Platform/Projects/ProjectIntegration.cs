using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class ProjectIntegration : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string IntegrationType { get; set; } = null!; // oauth | payment | email | sms | storage | analytics
    [Required] public string Provider { get; set; } = null!;
    [Required] public string ConfigJson { get; set; } = "{}";

    public Project? Project { get; set; }
}
