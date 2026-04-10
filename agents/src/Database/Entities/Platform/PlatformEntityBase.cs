using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform;

/// <summary>
/// Base class for all platform metadata entities.
/// Provides tenant isolation, audit columns, soft-delete, and optimistic concurrency.
/// </summary>
public abstract class PlatformEntityBase
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = "system";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = "system";
    public int VersionNo { get; set; } = 1;
}
