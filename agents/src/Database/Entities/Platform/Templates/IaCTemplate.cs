using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class IaCTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    public string? CloudProviderId { get; set; }
    [Required] public string Tool { get; set; } = null!; // terraform | pulumi | cdk | bicep
    [Required] public string TemplateContent { get; set; } = string.Empty;

    public CloudProvider? CloudProvider { get; set; }
}
