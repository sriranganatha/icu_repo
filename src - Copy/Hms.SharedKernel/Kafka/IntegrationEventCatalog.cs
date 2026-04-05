namespace Hms.SharedKernel.Kafka;

/// <summary>
/// Canonical catalog of all integration events flowing through Kafka.
/// Used for documentation, schema validation, and consumer routing.
/// </summary>
public static class IntegrationEventCatalog
{
    // Patient Service
    public const string PatientCreated       = "patient.patient_profile.created";
    public const string PatientUpdated       = "patient.patient_profile.updated";
    public const string PatientIdentifierAdded = "patient.patient_identifier.created";

    // Encounter Service
    public const string EncounterCreated     = "encounter.encounter.created";
    public const string EncounterUpdated     = "encounter.encounter.updated";
    public const string ClinicalNoteAdded    = "encounter.clinical_note.created";

    // Inpatient Service
    public const string AdmissionCreated     = "inpatient.admission.created";
    public const string EligibilityDecided   = "inpatient.admission_eligibility.created";

    // Emergency Service
    public const string ArrivalRegistered    = "emergency.emergency_arrival.created";
    public const string TriageCompleted      = "emergency.triage_assessment.created";

    // Diagnostics Service
    public const string ResultAvailable      = "diagnostics.result_record.created";

    // Revenue Service
    public const string ClaimSubmitted       = "revenue.claim.created";
    public const string ClaimUpdated         = "revenue.claim.updated";

    // AI Service
    public const string AiInteractionLogged  = "ai.ai_interaction.created";
}