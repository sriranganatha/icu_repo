using Microsoft.EntityFrameworkCore;
using Hms.PatientService.Data.Entities;

namespace Hms.PatientService.Data;

public class PatientServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public PatientServiceDbContext(DbContextOptions<PatientServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
        public DbSet<PatientIdentifier> PatientIdentifiers => Set<PatientIdentifier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: cl_mpi
        modelBuilder.Entity<PatientProfile>().ToTable("patient_profile", "cl_mpi");
            modelBuilder.Entity<PatientIdentifier>().ToTable("patient_identifier", "cl_mpi");

        // Tenant isolation query filters
        modelBuilder.Entity<PatientProfile>().HasQueryFilter(x => x.TenantId == _tenantId);
            modelBuilder.Entity<PatientIdentifier>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }