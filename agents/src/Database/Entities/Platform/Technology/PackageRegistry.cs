using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class PackageRegistry : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string LanguageId { get; set; } = null!;
    [Required] public string Url { get; set; } = null!;
    [Required] public string AuthType { get; set; } = "none"; // none | api_key | token | oauth

    public Language? Language { get; set; }
}
