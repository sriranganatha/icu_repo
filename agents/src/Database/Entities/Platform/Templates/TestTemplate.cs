using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class TestTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string TestType { get; set; } = null!; // unit | integration | e2e
    public string? FrameworkId { get; set; }
    [Required] public string TestFramework { get; set; } = null!; // xunit | nunit | jest | pytest
    [Required] public string TemplateContent { get; set; } = string.Empty;

    public Framework? Framework { get; set; }
}
