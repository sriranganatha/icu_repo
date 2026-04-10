using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Technology;

public class ArchitectureTemplate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Pattern { get; set; } = null!; // monolith | microservices | serverless | modular_monolith
    public string? DiagramTemplate { get; set; } // Mermaid template
}
