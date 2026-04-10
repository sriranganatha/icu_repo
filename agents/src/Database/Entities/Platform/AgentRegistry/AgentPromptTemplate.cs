using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.AgentRegistry;

public class AgentPromptTemplate : PlatformEntityBase
{
    [Required] public string AgentTypeDefinitionId { get; set; } = null!;
    [Required] public string PromptType { get; set; } = "system"; // system | task | review
    [Required] public string PromptTemplateText { get; set; } = string.Empty;
    public int PromptVersion { get; set; } = 1;

    public AgentTypeDefinition? AgentTypeDefinition { get; set; }
}
