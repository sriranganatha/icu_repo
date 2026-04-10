using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Standards;

public class SecurityPolicy : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Category { get; set; } = null!; // auth | data | network | compliance
    [Required] public string RulesJson { get; set; } = "[]";
    [Required] public string Severity { get; set; } = "medium"; // low | medium | high | critical
}
