namespace GNex.Services.Dtos.Platform;

// ─── LLM Config DTOs ────────────────────────────────────────
public sealed record LlmProviderDto(
    string Id, string Name, string ApiBaseUrl, string AuthType,
    int RateLimitPerMinute, bool IsAvailable, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    List<LlmModelDto> Models);

public sealed record LlmModelDto(
    string Id, string ProviderId, string ModelName,
    int ContextWindow, decimal CostInputPer1kTokens, decimal CostOutputPer1kTokens,
    string CapabilitiesJson, bool IsActive);

public sealed record LlmRoutingRuleDto(
    string Id, string TaskType, string PrimaryModelId,
    string? FallbackModelId, string? ConditionsJson, int Priority, bool IsActive);

public sealed record TokenBudgetDto(
    string Id, string Scope, long BudgetTokens, double AlertThreshold,
    string? ProjectId, bool IsActive);

// ─── Create / Update Requests ───────────────────────────────
public sealed record CreateLlmProviderRequest(
    string Name, string ApiBaseUrl, string AuthType, int RateLimitPerMinute);

public sealed record UpdateLlmProviderRequest(
    string Id, string Name, string ApiBaseUrl, string AuthType,
    int RateLimitPerMinute, bool IsAvailable);

public sealed record CreateLlmModelRequest(
    string ProviderId, string ModelName, int ContextWindow,
    decimal CostInputPer1kTokens, decimal CostOutputPer1kTokens, string CapabilitiesJson);

public sealed record UpdateLlmModelRequest(
    string Id, string ModelName, int ContextWindow,
    decimal CostInputPer1kTokens, decimal CostOutputPer1kTokens, string CapabilitiesJson);

public sealed record CreateRoutingRuleRequest(
    string TaskType, string PrimaryModelId, string? FallbackModelId,
    string? ConditionsJson, int Priority);

public sealed record CreateTokenBudgetRequest(
    string Scope, long BudgetTokens, double AlertThreshold, string? ProjectId);
