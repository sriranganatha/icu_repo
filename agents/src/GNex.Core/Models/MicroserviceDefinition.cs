using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// Defines a bounded-context microservice with its own database schema,
/// entities, API surface, and Docker-compose service entry.
/// </summary>
public sealed class MicroserviceDefinition
{
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required string Schema { get; init; }
    public required string Description { get; init; }
    public required int ApiPort { get; init; }
    public required string[] Entities { get; init; }
    public required string[] DependsOn { get; init; }
    public string Namespace => $"GNex.{Name}";
    public string ProjectName => $"GNex.{Name}";
    public string DbContextName => $"{Name}DbContext";
}

/// <summary>
/// Central registry of all HMS microservices aligned to bounded contexts.
/// Each service owns its schema, entities, and API surface.
/// </summary>
public static class MicroserviceCatalog
{
    public static readonly MicroserviceDefinition[] All =
    [
        new()
        {
            Name = "PatientService",
            ShortName = "patient",
            Schema = "cl_mpi",
            Description = "Master Patient Index — patient demographics, identifiers, and matching",
            ApiPort = 5101,
            Entities = ["PatientProfile", "PatientIdentifier"],
            DependsOn = []
        },
        new()
        {
            Name = "EncounterService",
            ShortName = "encounter",
            Schema = "cl_encounter",
            Description = "Clinical encounters, clinical notes, and visit tracking",
            ApiPort = 5102,
            Entities = ["Encounter", "ClinicalNote"],
            DependsOn = ["PatientService"]
        },
        new()
        {
            Name = "InpatientService",
            ShortName = "inpatient",
            Schema = "cl_inpatient",
            Description = "Admission, discharge, transfer (ADT) and bed management",
            ApiPort = 5103,
            Entities = ["Admission", "AdmissionEligibility"],
            DependsOn = ["PatientService", "EncounterService"]
        },
        new()
        {
            Name = "EmergencyService",
            ShortName = "emergency",
            Schema = "cl_emergency",
            Description = "Emergency arrivals, triage assessments, and ED tracking",
            ApiPort = 5104,
            Entities = ["EmergencyArrival", "TriageAssessment"],
            DependsOn = ["PatientService"]
        },
        new()
        {
            Name = "DiagnosticsService",
            ShortName = "diagnostics",
            Schema = "cl_diagnostics",
            Description = "Lab results, orders, and diagnostic records",
            ApiPort = 5105,
            Entities = ["ResultRecord"],
            DependsOn = ["PatientService", "EncounterService"]
        },
        new()
        {
            Name = "RevenueService",
            ShortName = "revenue",
            Schema = "op_revenue",
            Description = "Claims, billing, payer reconciliation",
            ApiPort = 5106,
            Entities = ["Claim"],
            DependsOn = ["PatientService", "EncounterService"]
        },
        new()
        {
            Name = "AuditService",
            ShortName = "audit",
            Schema = "gov_audit",
            Description = "Immutable audit trail for compliance (HIPAA, SOC2)",
            ApiPort = 5107,
            Entities = ["AuditEvent"],
            DependsOn = []
        },
        new()
        {
            Name = "AiService",
            ShortName = "ai",
            Schema = "gov_ai",
            Description = "AI interaction governance, model versioning, human-in-the-loop tracking",
            ApiPort = 5108,
            Entities = ["AiInteraction"],
            DependsOn = ["PatientService", "EncounterService"]
        }
    ];

    public static MicroserviceDefinition? ByName(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static MicroserviceDefinition? BySchema(string schema) =>
        All.FirstOrDefault(s => s.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase));
}
