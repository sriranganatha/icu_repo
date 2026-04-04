namespace Hms.PatientService.Contracts;

public sealed record PatientProfileDto
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string FacilityId { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreatePatientProfileRequest
{
    public required string TenantId { get; init; }
    public required string FacilityId { get; init; }
}

public sealed record UpdatePatientProfileRequest
{
    public required string Id { get; init; }
    public string? StatusCode { get; init; }
}