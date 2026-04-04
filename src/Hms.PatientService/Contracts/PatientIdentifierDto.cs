namespace Hms.PatientService.Contracts;

public sealed record PatientIdentifierDto
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string FacilityId { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreatePatientIdentifierRequest
{
    public required string TenantId { get; init; }
    public required string FacilityId { get; init; }
}

public sealed record UpdatePatientIdentifierRequest
{
    public required string Id { get; init; }
    public string? StatusCode { get; init; }
}