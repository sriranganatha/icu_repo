using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Governance;

public class AuditEvent
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    public string? FacilityId { get; set; }
    [Required] public string EventType { get; set; } = null!;
    [Required] public string EntityType { get; set; } = null!;
    [Required] public string EntityId { get; set; } = null!;
    [Required] public string ActorType { get; set; } = null!;
    [Required] public string ActorId { get; set; } = null!;
    [Required] public string CorrelationId { get; set; } = null!;
    [Required] public string ClassificationCode { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}