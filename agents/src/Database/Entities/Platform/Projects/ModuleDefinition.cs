using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class ModuleDefinition : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string Name { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public string? Responsibilities { get; set; }
    [Required] public string DependenciesJson { get; set; } = "[]"; // ["Auth","Notifications"]

    public Project? Project { get; set; }
}
