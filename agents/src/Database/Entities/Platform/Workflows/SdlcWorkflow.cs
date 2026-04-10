using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Workflows;

public class SdlcWorkflow : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    public ICollection<StageDefinition> Stages { get; set; } = [];
}
