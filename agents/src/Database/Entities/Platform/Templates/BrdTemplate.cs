using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class BrdTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string ProjectType { get; set; } = null!; // web_app | api | mobile_app | ...
    [Required] public string SectionsJson { get; set; } = "[]"; // ordered list of section definitions
    public bool IsDefault { get; set; }
}
