namespace GNex.Services.Dtos.Platform;

// ─── Standards DTOs ─────────────────────────────────────────
public sealed record CodingStandardDto(
    string Id, string Name, string? LanguageId, string RulesJson,
    string? LinterConfig, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record NamingConventionDto(
    string Id, string Scope, string Pattern, string ExamplesJson,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record QualityGateDto(
    string Id, string Name, string GateType, string ThresholdConfigJson,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ReviewChecklistDto(
    string Id, string Name, string Scope, string ChecklistItemsJson,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record SecurityPolicyDto(
    string Id, string Name, string Category, string RulesJson, string Severity,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// ─── Create / Update Requests ───────────────────────────────
public sealed record CreateCodingStandardRequest(
    string Name, string? LanguageId, string RulesJson, string? LinterConfig);

public sealed record UpdateCodingStandardRequest(
    string Id, string Name, string? LanguageId, string RulesJson, string? LinterConfig);

public sealed record CreateNamingConventionRequest(
    string Scope, string Pattern, string ExamplesJson);

public sealed record CreateQualityGateRequest(
    string Name, string GateType, string ThresholdConfigJson);

public sealed record UpdateQualityGateRequest(
    string Id, string Name, string GateType, string ThresholdConfigJson);

public sealed record CreateReviewChecklistRequest(
    string Name, string Scope, string ChecklistItemsJson);

public sealed record CreateSecurityPolicyRequest(
    string Name, string Category, string RulesJson, string Severity);

public sealed record UpdateSecurityPolicyRequest(
    string Id, string Name, string Category, string RulesJson, string Severity);
