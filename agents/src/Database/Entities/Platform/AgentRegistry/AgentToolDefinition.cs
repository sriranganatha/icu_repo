using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.AgentRegistry;

public class AgentToolDefinition : PlatformEntityBase
{
    [Required] public string AgentTypeDefinitionId { get; set; } = null!;
    [Required] public string ToolName { get; set; } = null!; // e.g. "file_write", "shell", "git"
    [Required] public string ToolConfigJson { get; set; } = "{}";
    public bool IsRequired { get; set; }

    public AgentTypeDefinition? AgentTypeDefinition { get; set; }
}
