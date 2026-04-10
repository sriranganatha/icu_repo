using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.AgentRegistry;

public class AgentModelMapping : PlatformEntityBase
{
    [Required] public string AgentTypeDefinitionId { get; set; } = null!;
    [Required] public string LlmProvider { get; set; } = null!; // e.g. "gemini", "openai", "ollama"
    [Required] public string ModelId { get; set; } = null!; // e.g. "gemini-2.5-pro"
    public int TokenLimit { get; set; } = 8192;
    public decimal CostPer1kTokens { get; set; }

    public AgentTypeDefinition? AgentTypeDefinition { get; set; }
}
