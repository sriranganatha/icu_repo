namespace Hms.InpatientService.Contracts;

public sealed record AdmissionEligibilityDto
{

}

public sealed record CreateAdmissionEligibilityRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateAdmissionEligibilityRequest
{
    public required string Id { get; init; }

}