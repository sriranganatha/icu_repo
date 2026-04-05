namespace Hms.EncounterService.Contracts;

public sealed record EncounterDto
{

}

public sealed record CreateEncounterRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateEncounterRequest
{
    public required string Id { get; init; }

}