namespace Hms.SharedKernel.Kafka;

/// <summary>
/// Central Kafka topic naming convention.
/// Pattern: hms.{service}.events — one topic per bounded context.
/// Partition key: {tenant_id}:{entity_id} — ordering per entity per tenant.
/// </summary>
public static class KafkaTopics
{
    public const string Patient     = "hms.patient.events";
    public const string Encounter   = "hms.encounter.events";
    public const string Inpatient   = "hms.inpatient.events";
    public const string Emergency   = "hms.emergency.events";
    public const string Diagnostics = "hms.diagnostics.events";
    public const string Revenue     = "hms.revenue.events";
    public const string Audit       = "hms.audit.events";
    public const string Ai          = "hms.ai.events";

    /// <summary>Dead letter queue for failed event processing</summary>
    public const string Dlq = "hms.dlq";

    /// <summary>Resolve topic name from service short name.</summary>
    public static string For(string serviceShortName) =>
        $"hms.{serviceShortName}.events";

    public static readonly string[] All =
        [Patient, Encounter, Inpatient, Emergency, Diagnostics, Revenue, Audit, Ai, Dlq];
}