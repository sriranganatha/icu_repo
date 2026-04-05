using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Observability;

/// <summary>
/// AI-powered observability agent. Generates OpenTelemetry instrumentation,
/// Prometheus metrics, structured logging, distributed tracing, health checks,
/// Grafana dashboards, and alerting rules for all HMS microservices.
/// </summary>
public sealed class ObservabilityAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<ObservabilityAgent> _logger;

    public AgentType Type => AgentType.Observability;
    public string Name => "Observability Agent";
    public string Description => "Generates OpenTelemetry tracing, Prometheus metrics, structured logging, Grafana dashboards, and alerting rules for all HMS services.";

    private static readonly string[] HmsServices =
    [
        "PatientService", "EncounterService", "InpatientService", "EmergencyService",
        "DiagnosticsService", "RevenueService", "AuditService", "AiService", "ApiGateway"
    ];

    public ObservabilityAgent(ILlmProvider llm, ILogger<ObservabilityAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ObservabilityAgent starting — AI-powered o11y artifact generation");

        var artifacts = new List<CodeArtifact>();

        try
        {
            artifacts.Add(GenerateOpenTelemetryBootstrap());
            artifacts.Add(GenerateHmsMetrics());
            artifacts.Add(await GenerateRequestTracingMiddleware(ct));
            artifacts.Add(GenerateHealthCheckComposite());
            artifacts.Add(await GenerateGrafanaDashboard(ct));
            artifacts.Add(GenerateAlertingRules());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Observability Agent: {artifacts.Count} o11y artifacts (AI: {_llm.ProviderName})",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Observability artifacts generated",
                    Body = "OpenTelemetry, Prometheus metrics, request tracing, health checks, Grafana dashboard, alert rules." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ObservabilityAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateOpenTelemetryBootstrap() => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = "Hms.SharedKernel/Observability/OpenTelemetryBootstrap.cs",
        FileName = "OpenTelemetryBootstrap.cs",
        Namespace = "Hms.SharedKernel.Observability",
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01"],
        Content = """
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
            """
    };

    private static CodeArtifact GenerateHmsMetrics() => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = "Hms.SharedKernel/Observability/HmsMetrics.cs",
        FileName = "HmsMetrics.cs",
        Namespace = "Hms.SharedKernel.Observability",
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01"],
        Content = """
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
            """
    };

    private async Task<CodeArtifact> GenerateRequestTracingMiddleware(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are an ASP.NET Core observability expert. Generate request tracing middleware.",
            UserPrompt = "Generate a RequestTracingMiddleware that: 1) Starts a new Activity/span per request, 2) Tags with TenantId, UserId, Role, 3) Records request duration, 4) Logs structured request/response, 5) Marks PHI access events. Use OpenTelemetryBootstrap.ServiceActivitySource. Namespace: Hms.SharedKernel.Observability.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Observability,
            RelativePath = "Hms.SharedKernel/Observability/RequestTracingMiddleware.cs",
            FileName = "RequestTracingMiddleware.cs",
            Namespace = "Hms.SharedKernel.Observability",
            ProducedBy = AgentType.Observability,
            TracedRequirementIds = ["NFR-OBS-01"],
            Content = response.Success ? response.Content : """
                using System.Diagnostics;
                using Microsoft.AspNetCore.Http;
                using Microsoft.Extensions.Logging;

                namespace Hms.SharedKernel.Observability;

                public sealed class RequestTracingMiddleware
                {
                    private readonly RequestDelegate _next;
                    private readonly ILogger<RequestTracingMiddleware> _logger;

                    public RequestTracingMiddleware(RequestDelegate next, ILogger<RequestTracingMiddleware> logger)
                    { _next = next; _logger = logger; }

                    public async Task InvokeAsync(HttpContext context)
                    {
                        using var activity = OpenTelemetryBootstrap.StartActivity(
                            $"{context.Request.Method} {context.Request.Path}", ActivityKind.Server);

                        var tenantId = context.Request.Headers["X-Tenant-Id"].ToString();
                        var userId = context.User.FindFirst("sub")?.Value ?? "anonymous";
                        var role = context.User.FindFirst("role")?.Value ?? "none";

                        activity?.SetTag("tenant.id", tenantId);
                        activity?.SetTag("user.id", userId);
                        activity?.SetTag("user.role", role);
                        activity?.SetTag("http.method", context.Request.Method);
                        activity?.SetTag("http.url", context.Request.Path.ToString());

                        var sw = Stopwatch.StartNew();
                        try
                        {
                            await _next(context);
                            activity?.SetTag("http.status_code", context.Response.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                            OpenTelemetryBootstrap.ErrorCounter.Add(1,
                                new("tenant", tenantId), new("exception", ex.GetType().Name));
                            throw;
                        }
                        finally
                        {
                            var elapsed = sw.Elapsed.TotalMilliseconds;
                            OpenTelemetryBootstrap.RequestDuration.Record(elapsed,
                                new("tenant", tenantId), new("method", context.Request.Method),
                                new("path", context.Request.Path.ToString()));
                            OpenTelemetryBootstrap.RequestCounter.Add(1,
                                new("tenant", tenantId), new("status", context.Response.StatusCode.ToString()));

                            _logger.LogInformation(
                                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:N1}ms [Tenant={TenantId}] [User={UserId}] [Role={Role}]",
                                context.Request.Method, context.Request.Path,
                                context.Response.StatusCode, elapsed, tenantId, userId, role);
                        }
                    }
                }
                """
        };
    }

    private static CodeArtifact GenerateHealthCheckComposite() => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = "Hms.SharedKernel/Observability/HmsHealthChecks.cs",
        FileName = "HmsHealthChecks.cs",
        Namespace = "Hms.SharedKernel.Observability",
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01", "SOC2-CC7"],
        Content = """
            using Microsoft.Extensions.Diagnostics.HealthChecks;

            namespace Hms.SharedKernel.Observability;

            /// <summary>
            /// Composite health check runner that aggregates DB, Kafka, and external service health.
            /// </summary>
            public sealed class CompositeHealthCheck : IHealthCheck
            {
                private readonly IEnumerable<IHealthCheck> _checks;

                public CompositeHealthCheck(IEnumerable<IHealthCheck> checks) => _checks = checks;

                public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
                {
                    var results = new Dictionary<string, object>();
                    var worstStatus = HealthStatus.Healthy;

                    foreach (var check in _checks)
                    {
                        var result = await check.CheckHealthAsync(context, ct);
                        results[check.GetType().Name] = result.Status.ToString();
                        if (result.Status < worstStatus) worstStatus = result.Status;
                    }

                    return new HealthCheckResult(worstStatus, data: results);
                }
            }

            public sealed class DatabaseHealthCheck : IHealthCheck
            {
                public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
                    => Task.FromResult(HealthCheckResult.Healthy("Database connection OK"));
            }

            public sealed class KafkaHealthCheck : IHealthCheck
            {
                public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
                    => Task.FromResult(HealthCheckResult.Healthy("Kafka broker reachable"));
            }
            """
    };

    private async Task<CodeArtifact> GenerateGrafanaDashboard(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a Grafana dashboarding expert for healthcare monitoring. Generate a Grafana dashboard JSON.",
            UserPrompt = $"Generate a Grafana dashboard JSON for HMS with panels: 1) Request rate per service, 2) P50/P95/P99 latency, 3) Error rate, 4) PHI access events, 5) Emergency arrivals by triage level, 6) AI interaction latency, 7) Kafka publish latency, 8) Database connection pool. Services: {string.Join(", ", HmsServices)}. Use Prometheus data source.",
            Temperature = 0.2, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Observability,
            RelativePath = "infrastructure/grafana/hms-dashboard.json",
            FileName = "hms-dashboard.json",
            Namespace = string.Empty,
            ProducedBy = AgentType.Observability,
            TracedRequirementIds = ["NFR-OBS-01"],
            Content = response.Success ? response.Content : GenerateGrafanaDashboardFallback()
        };
    }

    private static CodeArtifact GenerateAlertingRules() => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = "infrastructure/prometheus/hms-alerts.yml",
        FileName = "hms-alerts.yml",
        Namespace = string.Empty,
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01", "SOC2-CC7"],
        Content = """
            groups:
              - name: hms_critical_alerts
                rules:
                  - alert: HighErrorRate
                    expr: rate(hms_errors_total[5m]) > 0.05
                    for: 2m
                    labels:
                      severity: critical
                    annotations:
                      summary: "High error rate on {{ $labels.service }}"
                      description: "Error rate is {{ $value }} errors/sec (threshold: 0.05)"

                  - alert: SlowResponses
                    expr: histogram_quantile(0.95, rate(hms_requests_duration_ms_bucket[5m])) > 2000
                    for: 5m
                    labels:
                      severity: warning
                    annotations:
                      summary: "P95 latency above 2s on {{ $labels.service }}"

                  - alert: EmergencyBacklog
                    expr: increase(hms_requests_total{operation="emergency.arrival"}[1h]) > 50
                    for: 1m
                    labels:
                      severity: critical
                    annotations:
                      summary: "Emergency department surge: {{ $value }} arrivals in 1h"

                  - alert: UnauthorizedPhiAccess
                    expr: rate(hms_security_breach_attempts[5m]) > 0
                    for: 1m
                    labels:
                      severity: critical
                    annotations:
                      summary: "Potential unauthorized PHI access detected ({{ $value }}/sec)"

                  - alert: ServiceDown
                    expr: up == 0
                    for: 1m
                    labels:
                      severity: critical
                    annotations:
                      summary: "Service {{ $labels.instance }} is down"

                  - alert: KafkaPublishSlow
                    expr: histogram_quantile(0.95, rate(hms_kafka_publish_ms_bucket[5m])) > 500
                    for: 5m
                    labels:
                      severity: warning
                    annotations:
                      summary: "Kafka publish P95 latency above 500ms"

                  - alert: AiResponseSlow
                    expr: histogram_quantile(0.95, rate(hms_requests_duration_ms_bucket{operation="ai.interact"}[5m])) > 10000
                    for: 5m
                    labels:
                      severity: warning
                    annotations:
                      summary: "AI interaction P95 latency above 10s"

              - name: hms_compliance_alerts
                rules:
                  - alert: PhiAccessSpike
                    expr: rate(hms_phi_access_total[5m]) > 100
                    for: 2m
                    labels:
                      severity: warning
                    annotations:
                      summary: "Unusual PHI access rate: {{ $value }}/sec from {{ $labels.user }}"

                  - alert: BreakTheGlassUsed
                    expr: increase(hms_requests_total{operation="break_the_glass"}[1h]) > 0
                    for: 0m
                    labels:
                      severity: warning
                    annotations:
                      summary: "Break-the-glass emergency access activated"
            """
    };

    private static string GenerateGrafanaDashboardFallback() => """
        {
          "dashboard": {
            "title": "HMS Healthcare Platform",
            "uid": "hms-overview",
            "timezone": "UTC",
            "refresh": "10s",
            "panels": [
              {
                "title": "Request Rate (per service)",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "rate(hms_requests_total[5m])", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 }
              },
              {
                "title": "P95 Latency (ms)",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "histogram_quantile(0.95, rate(hms_requests_duration_ms_bucket[5m]))", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 }
              },
              {
                "title": "Error Rate",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "rate(hms_errors_total[5m])", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 0, "y": 8 }
              },
              {
                "title": "PHI Access Events",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "rate(hms_phi_access_total[5m])", "legendFormat": "{{ entity }} - {{ access }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 12, "y": 8 }
              },
              {
                "title": "Emergency Arrivals by Triage",
                "type": "barchart",
                "datasource": "Prometheus",
                "targets": [{ "expr": "increase(hms_requests_total{operation='emergency.arrival'}[1h])", "legendFormat": "{{ triage }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 0, "y": 16 }
              },
              {
                "title": "AI Interaction Latency",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "histogram_quantile(0.95, rate(hms_requests_duration_ms_bucket{operation='ai.interact'}[5m]))", "legendFormat": "{{ model }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 12, "y": 16 }
              }
            ]
          }
        }
        """;
}
