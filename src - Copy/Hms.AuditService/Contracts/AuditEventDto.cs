namespace Hms.AuditService.Contracts;

public sealed record AuditEventDto
{

}

public sealed record CreateAuditEventRequest
{
    public required string TenantId { get; init; }
}

public sealed record UpdateAuditEventRequest
{
    public required string Id { get; init; }

}