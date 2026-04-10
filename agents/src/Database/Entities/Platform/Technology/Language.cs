using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Technology;

public class Language : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Version { get; set; } = null!;
    [Required] public string Status { get; set; } = "active"; // active | deprecated
    public string? Icon { get; set; }
    [Required] public string FileExtensionsJson { get; set; } = "[]"; // e.g. [".cs",".csx"]

    public ICollection<Framework> Frameworks { get; set; } = [];
    public ICollection<PackageRegistry> PackageRegistries { get; set; } = [];
    public ICollection<CodeTemplate> CodeTemplates { get; set; } = [];
    public ICollection<CiCdTemplate> CiCdTemplates { get; set; } = [];
    public ICollection<DockerTemplate> DockerTemplates { get; set; } = [];
    public ICollection<CodingStandard> CodingStandards { get; set; } = [];
}
