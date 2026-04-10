namespace GNex.Services.Dtos.Platform;

// ── Agent Registry DTOs ───────────────────────────────────
public sealed record AgentTypeDefinitionDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string AgentTypeCode { get; init; } = string.Empty;
    public string CapabilitiesJson { get; init; } = "[]";
    public string? DefaultModelId { get; init; }
    public List<AgentModelMappingDto> ModelMappings { get; init; } = [];
    public List<AgentToolDefinitionDto> Tools { get; init; } = [];
}

public sealed record AgentModelMappingDto
{
    public string Id { get; init; } = string.Empty;
    public string LlmProvider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public int TokenLimit { get; init; }
    public decimal CostPer1kTokens { get; init; }
}

public sealed record AgentToolDefinitionDto
{
    public string Id { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string ToolConfigJson { get; init; } = "{}";
    public bool IsRequired { get; init; }
}

public sealed record AgentPromptTemplateDto
{
    public string Id { get; init; } = string.Empty;
    public string PromptType { get; init; } = string.Empty;
    public string PromptTemplateText { get; init; } = string.Empty;
    public int PromptVersion { get; init; }
}

public sealed record CreateAgentTypeRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string AgentTypeCode { get; init; }
    public string CapabilitiesJson { get; init; } = "[]";
    public string? DefaultModelId { get; init; }
}

public sealed record UpdateAgentTypeRequest
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? CapabilitiesJson { get; init; }
    public string? DefaultModelId { get; init; }
}

public sealed record CreateAgentModelMappingRequest
{
    public required string AgentTypeDefinitionId { get; init; }
    public required string LlmProvider { get; init; }
    public required string ModelId { get; init; }
    public int TokenLimit { get; init; }
    public decimal CostPer1kTokens { get; init; }
}

public sealed record CreateAgentPromptRequest
{
    public required string AgentTypeDefinitionId { get; init; }
    public required string PromptType { get; init; }
    public required string PromptTemplateText { get; init; }
}
