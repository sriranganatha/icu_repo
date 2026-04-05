namespace Hms.InpatientService.Contracts;

public sealed record AdmissionDto
{

}

public sealed record CreateAdmissionRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateAdmissionRequest
{
    public required string Id { get; init; }

}