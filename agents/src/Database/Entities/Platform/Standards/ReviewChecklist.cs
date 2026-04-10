using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Standards;

public class ReviewChecklist : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Scope { get; set; } = null!; // code | architecture | security | performance
    [Required] public string ChecklistItemsJson { get; set; } = "[]"; // ["Check for SQL injection","Validate input bounds",...]
}
