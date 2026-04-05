using Microsoft.EntityFrameworkCore;
using Hms.InpatientService.Data.Entities;

namespace Hms.InpatientService.Data;

public class InpatientServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public InpatientServiceDbContext(DbContextOptions<InpatientServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<Admission> Admissions => Set<Admission>();
        public DbSet<AdmissionEligibility> AdmissionEligibilitys => Set<AdmissionEligibility>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: cl_inpatient
        modelBuilder.Entity<Admission>().ToTable("admission", "cl_inpatient");
            modelBuilder.Entity<AdmissionEligibility>().ToTable("admission_eligibility", "cl_inpatient");

        // Tenant isolation query filters
        modelBuilder.Entity<Admission>().HasQueryFilter(x => x.TenantId == _tenantId);
            modelBuilder.Entity<AdmissionEligibility>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }