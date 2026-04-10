using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Standards;

public class CodingStandard : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    public string? LanguageId { get; set; }
    [Required] public string RulesJson { get; set; } = "[]";
    public string? LinterConfig { get; set; } // raw linter config content (eslintrc, editorconfig, etc.)

    public Technology.Language? Language { get; set; }
}
