namespace Hms.PatientService.Contracts;

public sealed record PatientIdentifierDto
{

}

public sealed record CreatePatientIdentifierRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdatePatientIdentifierRequest
{
    public required string Id { get; init; }

}