namespace Hms.EmergencyService.Contracts;

public sealed record EmergencyArrivalDto
{

}

public sealed record CreateEmergencyArrivalRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateEmergencyArrivalRequest
{
    public required string Id { get; init; }

}