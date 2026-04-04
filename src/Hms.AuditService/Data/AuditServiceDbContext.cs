using Microsoft.EntityFrameworkCore;
using Hms.AuditService.Data.Entities;

namespace Hms.AuditService.Data;

public class AuditServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public AuditServiceDbContext(DbContextOptions<AuditServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: gov_audit
        modelBuilder.Entity<AuditEvent>().ToTable("audit_event", "gov_audit");

        // Tenant isolation query filters
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }