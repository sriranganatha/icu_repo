using Microsoft.EntityFrameworkCore;
using Hms.AiService.Data.Entities;

namespace Hms.AiService.Data;

public class AiServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public AiServiceDbContext(DbContextOptions<AiServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: gov_ai
        modelBuilder.Entity<AiInteraction>().ToTable("ai_interaction", "gov_ai");

        // Tenant isolation query filters
        modelBuilder.Entity<AiInteraction>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }