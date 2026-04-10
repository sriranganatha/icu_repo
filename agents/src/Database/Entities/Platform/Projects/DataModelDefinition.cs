using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class DataModelDefinition : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string EntityName { get; set; } = null!;
    [Required] public string FieldsJson { get; set; } = "[]"; // [{"name":"id","type":"string","required":true},...]
    public string? RelationshipsJson { get; set; } // [{"type":"one_to_many","target":"Order","fk":"customer_id"}]
    public string? IndexesJson { get; set; }

    public Project? Project { get; set; }
}
