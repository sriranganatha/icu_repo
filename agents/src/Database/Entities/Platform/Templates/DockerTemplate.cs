using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class DockerTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    public string? LanguageId { get; set; }
    public string? FrameworkId { get; set; }
    [Required] public string DockerfileContent { get; set; } = string.Empty;
    public string? ComposeContent { get; set; }

    public Language? Language { get; set; }
    public Framework? Framework { get; set; }
}
