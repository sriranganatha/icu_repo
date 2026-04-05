using Microsoft.EntityFrameworkCore;
using Hms.EncounterService.Data.Entities;

namespace Hms.EncounterService.Data;

public class EncounterServiceDbContext : DbContext
{
    private readonly string _tenantId;

    public EncounterServiceDbContext(DbContextOptions<EncounterServiceDbContext> options, ITenantProvider tenant)
        : base(options) => _tenantId = tenant.TenantId;

    public DbSet<Encounter> Encounters => Set<Encounter>();
        public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema: cl_encounter
        modelBuilder.Entity<Encounter>().ToTable("encounter", "cl_encounter");
            modelBuilder.Entity<ClinicalNote>().ToTable("clinical_note", "cl_encounter");

        // Tenant isolation query filters
        modelBuilder.Entity<Encounter>().HasQueryFilter(x => x.TenantId == _tenantId);
            modelBuilder.Entity<ClinicalNote>().HasQueryFilter(x => x.TenantId == _tenantId);
    }
}

public interface ITenantProvider { string TenantId { get; } }