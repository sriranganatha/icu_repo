using Microsoft.EntityFrameworkCore;
using Hms.EmergencyService.Data.Entities;

namespace Hms.EmergencyService.Data;

public class EmergencyServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public EmergencyServiceDbContext(DbContextOptions<EmergencyServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<EmergencyArrival> EmergencyArrivals => Set<EmergencyArrival>();
        public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: cl_emergency
        modelBuilder.Entity<EmergencyArrival>().ToTable("emergency_arrival", "cl_emergency");
            modelBuilder.Entity<TriageAssessment>().ToTable("triage_assessment", "cl_emergency");

        // Tenant isolation query filters
        modelBuilder.Entity<EmergencyArrival>().HasQueryFilter(x => x.TenantId == _tenantId);
            modelBuilder.Entity<TriageAssessment>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }