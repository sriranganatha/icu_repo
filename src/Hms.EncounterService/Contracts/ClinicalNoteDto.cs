namespace Hms.EncounterService.Contracts;

public sealed record ClinicalNoteDto
{

}

public sealed record CreateClinicalNoteRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateClinicalNoteRequest
{
    public required string Id { get; init; }

}