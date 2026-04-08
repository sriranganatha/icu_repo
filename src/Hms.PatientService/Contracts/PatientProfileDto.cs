namespace Hms.PatientService.Contracts;

public sealed record PatientProfileDto
{
    public required string Id { get; init; }
}

public sealed record CreatePatientProfileRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdatePatientProfileRequest
{
    public required string Id { get; init; }

}