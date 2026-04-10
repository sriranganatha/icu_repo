using GNex.Database;
using GNex.Database.Entities.Platform.LlmConfig;
using GNex.Database.Repositories;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

public sealed class LlmConfigService : ILlmConfigService
{
    private readonly IPlatformRepository<LlmProviderConfig> _providerRepo;
    private readonly GNexDbContext _db;
    private readonly ILogger<LlmConfigService> _logger;

    public LlmConfigService(
        IPlatformRepository<LlmProviderConfig> providerRepo,
        GNexDbContext db,
        ILogger<LlmConfigService> logger)
    {
        _providerRepo = providerRepo;
        _db = db;
        _logger = logger;
    }

    // ─── Providers ──────────────────────────────────────────
    public async Task<LlmProviderDto?> GetProviderAsync(string id, CancellationToken ct = default)
    {
        var p = await _db.LlmProviderConfigs
            .Include(x => x.Models)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? null : MapProvider(p);
    }

    public async Task<List<LlmProviderDto>> ListProvidersAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var providers = await _db.LlmProviderConfigs
            .Where(x => x.IsActive)
            .Include(x => x.Models)
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        return providers.Select(MapProvider).ToList();
    }

    public async Task<LlmProviderDto> CreateProviderAsync(CreateLlmProviderRequest request, CancellationToken ct = default)
    {
        var entity = new LlmProviderConfig
        {
            Name = request.Name,
            ApiBaseUrl = request.ApiBaseUrl,
            AuthType = request.AuthType,
            RateLimitPerMinute = request.RateLimitPerMinute
        };
        await _providerRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created LLM provider {Id} '{Name}'", entity.Id, entity.Name);
        return MapProvider(entity);
    }

    public async Task<LlmProviderDto> UpdateProviderAsync(UpdateLlmProviderRequest request, CancellationToken ct = default)
    {
        var entity = await _providerRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"LLM provider {request.Id} not found");
        entity.Name = request.Name;
        entity.ApiBaseUrl = request.ApiBaseUrl;
        entity.AuthType = request.AuthType;
        entity.RateLimitPerMinute = request.RateLimitPerMinute;
        entity.IsAvailable = request.IsAvailable;
        await _providerRepo.UpdateAsync(entity, ct);
        return MapProvider(entity);
    }

    public async Task DeleteProviderAsync(string id, CancellationToken ct = default)
        => await _providerRepo.SoftDeleteAsync(id, ct);

    // ─── Models ─────────────────────────────────────────────
    public async Task<LlmModelDto> AddModelAsync(CreateLlmModelRequest request, CancellationToken ct = default)
    {
        var entity = new LlmModelConfig
        {
            ProviderId = request.ProviderId,
            ModelName = request.ModelName,
            ContextWindow = request.ContextWindow,
            CostInputPer1kTokens = request.CostInputPer1kTokens,
            CostOutputPer1kTokens = request.CostOutputPer1kTokens,
            CapabilitiesJson = request.CapabilitiesJson
        };
        _db.LlmModelConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Added LLM model {Id} '{Model}' to provider {Provider}",
            entity.Id, entity.ModelName, entity.ProviderId);
        return MapModel(entity);
    }

    public async Task<LlmModelDto> UpdateModelAsync(UpdateLlmModelRequest request, CancellationToken ct = default)
    {
        var entity = await _db.LlmModelConfigs.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"LLM model {request.Id} not found");
        entity.ModelName = request.ModelName;
        entity.ContextWindow = request.ContextWindow;
        entity.CostInputPer1kTokens = request.CostInputPer1kTokens;
        entity.CostOutputPer1kTokens = request.CostOutputPer1kTokens;
        entity.CapabilitiesJson = request.CapabilitiesJson;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapModel(entity);
    }

    public async Task DeleteModelAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.LlmModelConfigs.FindAsync([id], ct);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<LlmModelDto>> ListModelsAsync(string? providerId = null, CancellationToken ct = default)
    {
        var query = _db.LlmModelConfigs.Where(m => m.IsActive);
        if (providerId is not null)
            query = query.Where(m => m.ProviderId == providerId);
        var models = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
        return models.Select(MapModel).ToList();
    }

    // ─── Routing Rules ──────────────────────────────────────
    public async Task<List<LlmRoutingRuleDto>> ListRoutingRulesAsync(CancellationToken ct = default)
    {
        var rules = await _db.LlmRoutingRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);
        return rules.Select(r => new LlmRoutingRuleDto(
            r.Id, r.TaskType, r.PrimaryModelId, r.FallbackModelId,
            r.ConditionsJson, r.Priority, r.IsActive)).ToList();
    }

    public async Task<LlmRoutingRuleDto> CreateRoutingRuleAsync(CreateRoutingRuleRequest request, CancellationToken ct = default)
    {
        var entity = new LlmRoutingRule
        {
            TaskType = request.TaskType,
            PrimaryModelId = request.PrimaryModelId,
            FallbackModelId = request.FallbackModelId,
            ConditionsJson = request.ConditionsJson,
            Priority = request.Priority
        };
        _db.LlmRoutingRules.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new LlmRoutingRuleDto(entity.Id, entity.TaskType, entity.PrimaryModelId,
            entity.FallbackModelId, entity.ConditionsJson, entity.Priority, entity.IsActive);
    }

    public async Task DeleteRoutingRuleAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.LlmRoutingRules.FindAsync([id], ct);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    // ─── Token Budgets ──────────────────────────────────────
    public async Task<List<TokenBudgetDto>> ListTokenBudgetsAsync(string? projectId = null, CancellationToken ct = default)
    {
        var query = _db.TokenBudgets.Where(b => b.IsActive);
        if (projectId is not null)
            query = query.Where(b => b.ProjectId == projectId);
        var budgets = await query.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
        return budgets.Select(b => new TokenBudgetDto(
            b.Id, b.Scope, b.BudgetTokens, b.AlertThreshold,
            b.ProjectId, b.IsActive)).ToList();
    }

    public async Task<TokenBudgetDto> CreateTokenBudgetAsync(CreateTokenBudgetRequest request, CancellationToken ct = default)
    {
        var entity = new TokenBudget
        {
            Scope = request.Scope,
            BudgetTokens = request.BudgetTokens,
            AlertThreshold = request.AlertThreshold,
            ProjectId = request.ProjectId
        };
        _db.TokenBudgets.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new TokenBudgetDto(entity.Id, entity.Scope, entity.BudgetTokens,
            entity.AlertThreshold, entity.ProjectId, entity.IsActive);
    }

    public async Task DeleteTokenBudgetAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.TokenBudgets.FindAsync([id], ct);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    // ─── Mapping ────────────────────────────────────────────
    private static LlmProviderDto MapProvider(LlmProviderConfig p) => new(
        p.Id, p.Name, p.ApiBaseUrl, p.AuthType,
        p.RateLimitPerMinute, p.IsAvailable, p.IsActive,
        p.CreatedAt, p.UpdatedAt,
        p.Models.Select(MapModel).ToList());

    private static LlmModelDto MapModel(LlmModelConfig m) => new(
        m.Id, m.ProviderId, m.ModelName, m.ContextWindow,
        m.CostInputPer1kTokens, m.CostOutputPer1kTokens,
        m.CapabilitiesJson, m.IsActive);
}
