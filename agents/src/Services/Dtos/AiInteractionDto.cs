namespace Hms.Services.Dtos.Ai;

public sealed record AiInteractionDto
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string FacilityId { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateAiInteractionRequest
{
    public required string FacilityId { get; init; }
}

public sealed record UpdateAiInteractionRequest
{
    public required string Id { get; init; }
    public string? StatusCode { get; init; }
}