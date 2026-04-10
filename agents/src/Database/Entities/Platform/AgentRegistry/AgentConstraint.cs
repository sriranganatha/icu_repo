using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.AgentRegistry;

public class AgentConstraint : PlatformEntityBase
{
    [Required] public string AgentTypeDefinitionId { get; set; } = null!;
    public int MaxTokens { get; set; } = 8192;
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 300;
    public string? SandboxConfigJson { get; set; }

    public AgentTypeDefinition? AgentTypeDefinition { get; set; }
}
