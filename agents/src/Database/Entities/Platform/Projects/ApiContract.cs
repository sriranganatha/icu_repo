using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class ApiContract : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    public string? ModuleId { get; set; }
    [Required] public string Endpoint { get; set; } = null!;
    [Required] public string Method { get; set; } = "GET"; // GET | POST | PUT | PATCH | DELETE
    public string? RequestSchemaJson { get; set; }
    public string? ResponseSchemaJson { get; set; }
    public bool AuthRequired { get; set; } = true;

    public Project? Project { get; set; }
    public ModuleDefinition? Module { get; set; }
}
