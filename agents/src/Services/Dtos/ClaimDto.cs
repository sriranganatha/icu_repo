namespace GNex.Services.Dtos.Revenue;

public sealed record ClaimDto
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string FacilityId { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateClaimRequest
{
    public required string FacilityId { get; init; }
}

public sealed record UpdateClaimRequest
{
    public required string Id { get; init; }
    public string? StatusCode { get; init; }
}