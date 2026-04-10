using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Standards;

public class NamingConvention : PlatformEntityBase
{
    [Required] public string Scope { get; set; } = null!; // file | class | function | variable | db_table | db_column | api_endpoint
    [Required] public string Pattern { get; set; } = null!; // e.g. "PascalCase", "snake_case", "kebab-case"
    [Required] public string ExamplesJson { get; set; } = "[]"; // ["PatientProfile","EncounterService"]
}
