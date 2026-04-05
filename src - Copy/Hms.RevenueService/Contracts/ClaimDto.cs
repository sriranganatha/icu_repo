namespace Hms.RevenueService.Contracts;

public sealed record ClaimDto
{

}

public sealed record CreateClaimRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateClaimRequest
{
    public required string Id { get; init; }

}