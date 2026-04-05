namespace Hms.EmergencyService.Contracts;

public sealed record TriageAssessmentDto
{

}

public sealed record CreateTriageAssessmentRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateTriageAssessmentRequest
{
    public required string Id { get; init; }

}