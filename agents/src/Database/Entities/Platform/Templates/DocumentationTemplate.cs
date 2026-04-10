using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class DocumentationTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string DocType { get; set; } = null!; // readme | adr | api_doc | runbook
    [Required] public string TemplateContent { get; set; } = string.Empty;
}
