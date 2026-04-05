using Microsoft.EntityFrameworkCore;
using Hms.DiagnosticsService.Data.Entities;

namespace Hms.DiagnosticsService.Data;

public class DiagnosticsServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public DiagnosticsServiceDbContext(DbContextOptions<DiagnosticsServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<ResultRecord> ResultRecords => Set<ResultRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: cl_diagnostics
        modelBuilder.Entity<ResultRecord>().ToTable("result_record", "cl_diagnostics");

        // Tenant isolation query filters
        modelBuilder.Entity<ResultRecord>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }