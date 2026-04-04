using Microsoft.EntityFrameworkCore;
using Hms.RevenueService.Data.Entities;

namespace Hms.RevenueService.Data;

public class RevenueServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public RevenueServiceDbContext(DbContextOptions<RevenueServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<Claim> Claims => Set<Claim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: op_revenue
        modelBuilder.Entity<Claim>().ToTable("claim", "op_revenue");

        // Tenant isolation query filters
        modelBuilder.Entity<Claim>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }