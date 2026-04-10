using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class CodeTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    public string? LanguageId { get; set; }
    public string? FrameworkId { get; set; }
    [Required] public string TemplateType { get; set; } = null!; // scaffold | component | module
    [Required] public string Content { get; set; } = string.Empty;
    [Required] public string VariablesJson { get; set; } = "[]"; // ["project_name","namespace"]

    public Language? Language { get; set; }
    public Framework? Framework { get; set; }
}
