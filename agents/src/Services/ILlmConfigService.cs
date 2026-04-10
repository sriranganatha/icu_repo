using GNex.Services.Dtos.Platform;

namespace GNex.Services.Platform;

public interface ILlmConfigService
{
    // Providers
    Task<LlmProviderDto?> GetProviderAsync(string id, CancellationToken ct = default);
    Task<List<LlmProviderDto>> ListProvidersAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<LlmProviderDto> CreateProviderAsync(CreateLlmProviderRequest request, CancellationToken ct = default);
    Task<LlmProviderDto> UpdateProviderAsync(UpdateLlmProviderRequest request, CancellationToken ct = default);
    Task DeleteProviderAsync(string id, CancellationToken ct = default);

    // Models
    Task<LlmModelDto> AddModelAsync(CreateLlmModelRequest request, CancellationToken ct = default);
    Task<LlmModelDto> UpdateModelAsync(UpdateLlmModelRequest request, CancellationToken ct = default);
    Task DeleteModelAsync(string id, CancellationToken ct = default);
    Task<List<LlmModelDto>> ListModelsAsync(string? providerId = null, CancellationToken ct = default);

    // Routing Rules
    Task<List<LlmRoutingRuleDto>> ListRoutingRulesAsync(CancellationToken ct = default);
    Task<LlmRoutingRuleDto> CreateRoutingRuleAsync(CreateRoutingRuleRequest request, CancellationToken ct = default);
    Task DeleteRoutingRuleAsync(string id, CancellationToken ct = default);

    // Token Budgets
    Task<List<TokenBudgetDto>> ListTokenBudgetsAsync(string? projectId = null, CancellationToken ct = default);
    Task<TokenBudgetDto> CreateTokenBudgetAsync(CreateTokenBudgetRequest request, CancellationToken ct = default);
    Task DeleteTokenBudgetAsync(string id, CancellationToken ct = default);
}
