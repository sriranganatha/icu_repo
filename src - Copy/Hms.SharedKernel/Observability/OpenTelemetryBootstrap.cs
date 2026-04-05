using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Hms.SharedKernel.Observability;

/// <summary>
/// Centralized OpenTelemetry bootstrap for all HMS microservices.
/// Call AddHmsObservability() in each service's Program.cs.
/// </summary>
public static class OpenTelemetryBootstrap
{
    public const string ServiceActivitySourceName = "Hms.Service";
    public const string MeterName = "Hms.Metrics";

    public static readonly ActivitySource ServiceActivitySource = new(ServiceActivitySourceName);
    public static readonly Meter ServiceMeter = new(MeterName, "1.0.0");

    // Standard counters
    public static readonly Counter<long> RequestCounter =
        ServiceMeter.CreateCounter<long>("hms.requests.total", "request", "Total HTTP requests");
    public static readonly Histogram<double> RequestDuration =
        ServiceMeter.CreateHistogram<double>("hms.requests.duration_ms", "ms", "Request duration in ms");
    public static readonly Counter<long> ErrorCounter =
        ServiceMeter.CreateCounter<long>("hms.errors.total", "error", "Total errors");

    // Healthcare-specific metrics
    public static readonly Counter<long> PhiAccessCounter =
        ServiceMeter.CreateCounter<long>("hms.phi.access_total", "access", "Total PHI access events");
    public static readonly Counter<long> BreachAttemptCounter =
        ServiceMeter.CreateCounter<long>("hms.security.breach_attempts", "attempt", "Potential breach attempts");
    public static readonly Histogram<double> KafkaPublishLatency =
        ServiceMeter.CreateHistogram<double>("hms.kafka.publish_ms", "ms", "Kafka publish latency");

    /// <summary>
    /// Creates a new Activity (span) for tracing a service operation.
    /// </summary>
    public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
        => ServiceActivitySource.StartActivity(operationName, kind);
}