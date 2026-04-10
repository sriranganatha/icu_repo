using GNex.Database;
using GNex.Database.Entities.Platform.AgentRegistry;
using GNex.Database.Repositories;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

public sealed class AgentRegistryService : IAgentRegistryService
{
    private readonly IAgentRegistryRepository _repo;
    private readonly GNexDbContext _db;
    private readonly ILogger<AgentRegistryService> _logger;

    public AgentRegistryService(IAgentRegistryRepository repo, GNexDbContext db, ILogger<AgentRegistryService> logger)
    {
        _repo = repo;
        _db = db;
        _logger = logger;
    }

    public async Task<AgentTypeDefinitionDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var e = await _repo.GetWithConfigAsync(id, ct);
        return e is null ? null : MapFull(e);
    }

    public async Task<AgentTypeDefinitionDto?> GetByCodeAsync(string agentTypeCode, CancellationToken ct = default)
    {
        var e = await _repo.GetByAgentTypeCodeAsync(agentTypeCode, ct);
        return e is null ? null : MapFull(e);
    }

    public async Task<List<AgentTypeDefinitionDto>> ListAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var items = await _repo.ListWithMappingsAsync(skip, take, ct);
        return items.Select(MapFull).ToList();
    }

    public async Task<AgentTypeDefinitionDto> CreateAsync(CreateAgentTypeRequest request, CancellationToken ct = default)
    {
        var entity = new AgentTypeDefinition
        {
            Name = request.Name,
            Description = request.Description,
            AgentTypeCode = request.AgentTypeCode,
            CapabilitiesJson = request.CapabilitiesJson,
            DefaultModelId = request.DefaultModelId
        };
        await _repo.CreateAsync(entity, ct);
        _logger.LogInformation("Created agent type definition {Name} ({Code})", entity.Name, entity.AgentTypeCode);
        return MapFull(entity);
    }

    public async Task<AgentTypeDefinitionDto> UpdateAsync(UpdateAgentTypeRequest request, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"AgentTypeDefinition {request.Id} not found");
        if (request.Name is not null) entity.Name = request.Name;
        if (request.Description is not null) entity.Description = request.Description;
        if (request.CapabilitiesJson is not null) entity.CapabilitiesJson = request.CapabilitiesJson;
        if (request.DefaultModelId is not null) entity.DefaultModelId = request.DefaultModelId;
        await _repo.UpdateAsync(entity, ct);
        return MapFull(entity);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => _repo.SoftDeleteAsync(id, ct);

    public async Task<AgentModelMappingDto> AddModelMappingAsync(CreateAgentModelMappingRequest request, CancellationToken ct = default)
    {
        var mapping = new AgentModelMapping
        {
            AgentTypeDefinitionId = request.AgentTypeDefinitionId,
            LlmProvider = request.LlmProvider,
            ModelId = request.ModelId,
            TokenLimit = request.TokenLimit,
            CostPer1kTokens = request.CostPer1kTokens
        };
        _db.AgentModelMappings.Add(mapping);
        await _db.SaveChangesAsync(ct);
        return new AgentModelMappingDto
        {
            Id = mapping.Id, LlmProvider = mapping.LlmProvider,
            ModelId = mapping.ModelId, TokenLimit = mapping.TokenLimit,
            CostPer1kTokens = mapping.CostPer1kTokens
        };
    }

    public async Task<List<AgentPromptTemplateDto>> ListPromptsAsync(string agentTypeId, CancellationToken ct = default)
    {
        var prompts = await _db.AgentPromptTemplates
            .Where(p => p.AgentTypeDefinitionId == agentTypeId && p.IsActive)
            .OrderByDescending(p => p.PromptVersion)
            .ToListAsync(ct);
        return prompts.Select(p => new AgentPromptTemplateDto
        {
            Id = p.Id, PromptType = p.PromptType,
            PromptTemplateText = p.PromptTemplateText, PromptVersion = p.PromptVersion
        }).ToList();
    }

    public async Task<AgentPromptTemplateDto> AddPromptAsync(CreateAgentPromptRequest request, CancellationToken ct = default)
    {
        var latestVersion = await _db.AgentPromptTemplates
            .Where(p => p.AgentTypeDefinitionId == request.AgentTypeDefinitionId && p.PromptType == request.PromptType)
            .MaxAsync(p => (int?)p.PromptVersion, ct) ?? 0;

        var prompt = new AgentPromptTemplate
        {
            AgentTypeDefinitionId = request.AgentTypeDefinitionId,
            PromptType = request.PromptType,
            PromptTemplateText = request.PromptTemplateText,
            PromptVersion = latestVersion + 1
        };
        _db.AgentPromptTemplates.Add(prompt);
        await _db.SaveChangesAsync(ct);
        return new AgentPromptTemplateDto
        {
            Id = prompt.Id, PromptType = prompt.PromptType,
            PromptTemplateText = prompt.PromptTemplateText, PromptVersion = prompt.PromptVersion
        };
    }

    private static AgentTypeDefinitionDto MapFull(AgentTypeDefinition e) => new()
    {
        Id = e.Id, Name = e.Name, Description = e.Description,
        AgentTypeCode = e.AgentTypeCode, CapabilitiesJson = e.CapabilitiesJson,
        DefaultModelId = e.DefaultModelId,
        ModelMappings = e.ModelMappings?.Select(m => new AgentModelMappingDto
        {
            Id = m.Id, LlmProvider = m.LlmProvider, ModelId = m.ModelId,
            TokenLimit = m.TokenLimit, CostPer1kTokens = m.CostPer1kTokens
        }).ToList() ?? [],
        Tools = e.Tools?.Select(t => new AgentToolDefinitionDto
        {
            Id = t.Id, ToolName = t.ToolName, ToolConfigJson = t.ToolConfigJson, IsRequired = t.IsRequired
        }).ToList() ?? []
    };
}
