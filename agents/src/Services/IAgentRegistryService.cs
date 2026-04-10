using GNex.Services.Dtos.Platform;

namespace GNex.Services.Platform;

public interface IAgentRegistryService
{
    Task<AgentTypeDefinitionDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<AgentTypeDefinitionDto?> GetByCodeAsync(string agentTypeCode, CancellationToken ct = default);
    Task<List<AgentTypeDefinitionDto>> ListAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<AgentTypeDefinitionDto> CreateAsync(CreateAgentTypeRequest request, CancellationToken ct = default);
    Task<AgentTypeDefinitionDto> UpdateAsync(UpdateAgentTypeRequest request, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    // Model Mappings
    Task<AgentModelMappingDto> AddModelMappingAsync(CreateAgentModelMappingRequest request, CancellationToken ct = default);

    // Prompts
    Task<List<AgentPromptTemplateDto>> ListPromptsAsync(string agentTypeId, CancellationToken ct = default);
    Task<AgentPromptTemplateDto> AddPromptAsync(CreateAgentPromptRequest request, CancellationToken ct = default);
}
