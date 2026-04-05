namespace Hms.DiagnosticsService.Contracts;

public sealed record ResultRecordDto
{

}

public sealed record CreateResultRecordRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateResultRecordRequest
{
    public required string Id { get; init; }

}