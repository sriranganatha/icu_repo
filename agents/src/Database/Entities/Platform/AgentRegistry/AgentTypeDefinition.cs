using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.AgentRegistry;

/// <summary>
/// DB-backed definition of an agent type. Maps to the AgentType enum but allows
/// runtime configuration of capabilities, I/O schema, and default LLM model.
/// </summary>
public class AgentTypeDefinition : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string Description { get; set; } = string.Empty;
    [Required] public string CapabilitiesJson { get; set; } = "[]"; // ["code_generation","review","testing"]
    public string? InputSchemaJson { get; set; }
    public string? OutputSchemaJson { get; set; }
    public string? DefaultModelId { get; set; }
    [Required] public string AgentTypeCode { get; set; } = null!; // maps to AgentType enum name

    public ICollection<AgentModelMapping> ModelMappings { get; set; } = [];
    public ICollection<AgentToolDefinition> Tools { get; set; } = [];
    public ICollection<AgentPromptTemplate> Prompts { get; set; } = [];
    public ICollection<AgentConstraint> Constraints { get; set; } = [];
}
