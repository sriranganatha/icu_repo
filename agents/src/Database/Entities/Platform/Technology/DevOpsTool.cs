using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class DevOpsTool : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Category { get; set; } = null!; // ci_cd | container | iac | monitoring
    public string? ConfigTemplate { get; set; }
}
