using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class Framework : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string LanguageId { get; set; } = null!;
    [Required] public string Version { get; set; } = null!;
    [Required] public string Category { get; set; } = null!; // web | mobile | data | ml | desktop
    public string? DocsUrl { get; set; }

    public Language? Language { get; set; }
    public ICollection<CodeTemplate> CodeTemplates { get; set; } = [];
    public ICollection<FileStructureTemplate> FileStructureTemplates { get; set; } = [];
    public ICollection<DockerTemplate> DockerTemplates { get; set; } = [];
    public ICollection<TestTemplate> TestTemplates { get; set; } = [];
}
