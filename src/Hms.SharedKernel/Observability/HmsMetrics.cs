namespace Hms.SharedKernel.Observability;

/// <summary>
/// Healthcare-domain Prometheus metrics for clinical and operational monitoring.
/// </summary>
public static class HmsMetrics
{
    // Patient metrics
    public static void RecordPatientRegistration(string tenantId)
        => OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "patient.register"), new("tenant", tenantId));

    public static void RecordEncounterCreated(string tenantId, string encounterType)
        => OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "encounter.create"), new("tenant", tenantId), new("type", encounterType));

    // Emergency metrics (time-critical)
    public static void RecordEmergencyArrival(string tenantId, string triageLevel)
        => OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "emergency.arrival"), new("tenant", tenantId), new("triage", triageLevel));

    // Diagnostics metrics
    public static void RecordOrderCreated(string tenantId, string orderType)
        => OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "order.create"), new("tenant", tenantId), new("type", orderType));

    public static void RecordResultReceived(string tenantId, bool isCritical)
        => OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "result.receive"), new("tenant", tenantId), new("critical", isCritical.ToString()));

    // Revenue metrics
    public static void RecordClaimSubmitted(string tenantId, decimal amount)
        => OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "claim.submit"), new("tenant", tenantId));

    // AI metrics
    public static void RecordAiInteraction(string tenantId, string model, double latencyMs)
    {
        OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "ai.interact"), new("tenant", tenantId), new("model", model));
        OpenTelemetryBootstrap.RequestDuration.Record(latencyMs, new("operation", "ai.interact"), new("model", model));
    }

    // Security metrics
    public static void RecordPhiAccess(string tenantId, string entityType, string accessType)
        => OpenTelemetryBootstrap.PhiAccessCounter.Add(1, new("tenant", tenantId), new("entity", entityType), new("access", accessType));

    public static void RecordBreachAttempt(string tenantId, string reason)
        => OpenTelemetryBootstrap.BreachAttemptCounter.Add(1, new("tenant", tenantId), new("reason", reason));
}