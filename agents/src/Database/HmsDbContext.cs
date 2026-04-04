using Microsoft.EntityFrameworkCore;
using Hms.Database.Entities.Mpi;
using Hms.Database.Entities.Clinical;
using Hms.Database.Entities.Inpatient;
using Hms.Database.Entities.Emergency;
using Hms.Database.Entities.Diagnostics;
using Hms.Database.Entities.Revenue;
using Hms.Database.Entities.Governance;
using Hms.Database.Entities.Ai;

namespace Hms.Database;

public class HmsDbContext : DbContext
{
    private readonly string _tenantId;

    public HmsDbContext(DbContextOptions<HmsDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantId = tenantProvider.TenantId;
    }

    // MPI
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<PatientIdentifier> PatientIdentifiers => Set<PatientIdentifier>();

    // Clinical
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();

    // Inpatient
    public DbSet<Admission> Admissions => Set<Admission>();
    public DbSet<AdmissionEligibility> AdmissionEligibilities => Set<AdmissionEligibility>();

    // Emergency
    public DbSet<EmergencyArrival> EmergencyArrivals => Set<EmergencyArrival>();
    public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();

    // Diagnostics
    public DbSet<ResultRecord> ResultRecords => Set<ResultRecord>();

    // Revenue
    public DbSet<Claim> Claims => Set<Claim>();

    // Governance
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // AI
    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema separation by bounded context
        modelBuilder.Entity<PatientProfile>().ToTable("patient_profile", "cl_mpi");
        modelBuilder.Entity<PatientIdentifier>().ToTable("patient_identifier", "cl_mpi");
        modelBuilder.Entity<Encounter>().ToTable("encounter", "cl_encounter");
        modelBuilder.Entity<ClinicalNote>().ToTable("clinical_note", "cl_encounter");
        modelBuilder.Entity<Admission>().ToTable("admission", "cl_inpatient");
        modelBuilder.Entity<AdmissionEligibility>().ToTable("admission_eligibility", "cl_inpatient");
        modelBuilder.Entity<EmergencyArrival>().ToTable("emergency_arrival", "cl_emergency");
        modelBuilder.Entity<TriageAssessment>().ToTable("triage_assessment", "cl_emergency");
        modelBuilder.Entity<ResultRecord>().ToTable("result_record", "cl_diagnostics");
        modelBuilder.Entity<Claim>().ToTable("claim", "op_revenue");
        modelBuilder.Entity<AuditEvent>().ToTable("audit_event", "gov_audit");
        modelBuilder.Entity<AiInteraction>().ToTable("ai_interaction", "gov_ai");

        // Tenant-scoped global query filters
        modelBuilder.Entity<PatientProfile>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Encounter>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Admission>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<EmergencyArrival>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<ResultRecord>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Claim>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<AiInteraction>().HasQueryFilter(e => e.TenantId == _tenantId);

        // Unique constraints include tenant_id
        modelBuilder.Entity<PatientProfile>()
            .HasIndex(e => new { e.TenantId, e.EnterprisePersonKey }).IsUnique();
        modelBuilder.Entity<PatientIdentifier>()
            .HasIndex(e => new { e.TenantId, e.IdentifierType, e.IdentifierValueHash }).IsUnique();
    }
}

public interface ITenantProvider
{
    string TenantId { get; }
}