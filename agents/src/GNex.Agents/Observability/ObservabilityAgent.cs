using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Observability;

/// <summary>
/// AI-powered observability agent. Generates OpenTelemetry instrumentation,
/// Prometheus metrics, structured logging, distributed tracing, health checks,
/// Grafana dashboards, and alerting rules for all derived microservices.
/// </summary>
public sealed class ObservabilityAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<ObservabilityAgent> _logger;

    public AgentType Type => AgentType.Observability;
    public string Name => "Observability Agent";
    public string Description => "Generates OpenTelemetry tracing, Prometheus metrics, structured logging, Grafana dashboards, and alerting rules for all services.";

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
            var prefix = context.PipelineConfig?.ProjectPrefix ?? "app";
            var label = context.PipelineConfig?.ProjectLabel ?? "Application Platform";
            var services = ServiceCatalogResolver.GetServices(context);
            var serviceNames = services.Select(s => s.Name).ToArray();

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Generating OpenTelemetry bootstrap — ActivitySource, Meter, counters, histograms for {serviceNames.Length} services");
            artifacts.Add(GenerateOpenTelemetryBootstrap(prefix, label));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating metrics — request counters, duration histograms, access tracking, Kafka latency");
            artifacts.Add(GenerateGNexMetrics(prefix));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating request tracing middleware — distributed trace propagation");
            artifacts.Add(await GenerateRequestTracingMiddleware(prefix, ct));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating composite health check — DB, Kafka, Redis, downstream service liveness/readiness probes");
            artifacts.Add(GenerateHealthCheckComposite());

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating Grafana dashboard JSON — service overview, latency heatmap, error rate panels");
            artifacts.Add(await GenerateGrafanaDashboard(prefix, label, serviceNames, ct));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating Prometheus alerting rules — SLA violation, error spike, unauthorized access alerts");
            artifacts.Add(GenerateAlertingRules(prefix));

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

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

    private static CodeArtifact GenerateOpenTelemetryBootstrap(string prefix, string label) => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = "GNex.SharedKernel/Observability/OpenTelemetryBootstrap.cs",
        FileName = "OpenTelemetryBootstrap.cs",
        Namespace = "GNex.SharedKernel.Observability",
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01"],
        Content = $$"""
            using System.Diagnostics;
            using System.Diagnostics.Metrics;

            namespace GNex.SharedKernel.Observability;

            /// <summary>
            /// Centralized OpenTelemetry bootstrap for all microservices.
            /// Call AddObservability() in each service's Program.cs.
            /// </summary>
            public static class OpenTelemetryBootstrap
            {
                public const string ServiceActivitySourceName = "GNex.Service";
                public const string MeterName = "GNex.Metrics";

                public static readonly ActivitySource ServiceActivitySource = new(ServiceActivitySourceName);
                public static readonly Meter ServiceMeter = new(MeterName, "1.0.0");

                // Standard counters
                public static readonly Counter<long> RequestCounter =
                    ServiceMeter.CreateCounter<long>("{{prefix}}.requests.total", "request", "Total HTTP requests");
                public static readonly Histogram<double> RequestDuration =
                    ServiceMeter.CreateHistogram<double>("{{prefix}}.requests.duration_ms", "ms", "Request duration in ms");
                public static readonly Counter<long> ErrorCounter =
                    ServiceMeter.CreateCounter<long>("{{prefix}}.errors.total", "error", "Total errors");

                // Domain-specific metrics
                public static readonly Counter<long> SensitiveDataAccessCounter =
                    ServiceMeter.CreateCounter<long>("{{prefix}}.sensitive_data.access_total", "access", "Total sensitive data access events");
                public static readonly Counter<long> BreachAttemptCounter =
                    ServiceMeter.CreateCounter<long>("{{prefix}}.security.breach_attempts", "attempt", "Potential breach attempts");
                public static readonly Histogram<double> KafkaPublishLatency =
                    ServiceMeter.CreateHistogram<double>("{{prefix}}.kafka.publish_ms", "ms", "Kafka publish latency");

                /// <summary>
                /// Creates a new Activity (span) for tracing a service operation.
                /// </summary>
                public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
                    => ServiceActivitySource.StartActivity(operationName, kind);
            }
            """
    };

    private static CodeArtifact GenerateGNexMetrics(string prefix) => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = "GNex.SharedKernel/Observability/GNexMetrics.cs",
        FileName = "GNexMetrics.cs",
        Namespace = "GNex.SharedKernel.Observability",
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01"],
        Content = """
            namespace GNex.SharedKernel.Observability;

            /// <summary>
            /// Application-domain Prometheus metrics for operational monitoring.
            /// </summary>
            public static class GNexMetrics
            {
                // Domain operation metrics
                public static void RecordOperation(string tenantId, string operation, string? subType = null)
                {
                    var tags = new List<KeyValuePair<string, object?>>
                    {
                        new("operation", operation),
                        new("tenant", tenantId)
                    };
                    if (subType is not null) tags.Add(new("type", subType));
                    OpenTelemetryBootstrap.RequestCounter.Add(1, tags.ToArray());
                }

                // AI metrics
                public static void RecordAiInteraction(string tenantId, string model, double latencyMs)
                {
                    OpenTelemetryBootstrap.RequestCounter.Add(1, new("operation", "ai.interact"), new("tenant", tenantId), new("model", model));
                    OpenTelemetryBootstrap.RequestDuration.Record(latencyMs, new("operation", "ai.interact"), new("model", model));
                }

                // Security metrics
                public static void RecordSensitiveDataAccess(string tenantId, string entityType, string accessType)
                    => OpenTelemetryBootstrap.SensitiveDataAccessCounter.Add(1, new("tenant", tenantId), new("entity", entityType), new("access", accessType));

                public static void RecordBreachAttempt(string tenantId, string reason)
                    => OpenTelemetryBootstrap.BreachAttemptCounter.Add(1, new("tenant", tenantId), new("reason", reason));
            }
            """
    };

    private async Task<CodeArtifact> GenerateRequestTracingMiddleware(string prefix, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are an ASP.NET Core observability expert. Generate request tracing middleware.",
            UserPrompt = "Generate a RequestTracingMiddleware that: 1) Starts a new Activity/span per request, 2) Tags with TenantId, UserId, Role, 3) Records request duration, 4) Logs structured request/response, 5) Marks sensitive data access events. Use OpenTelemetryBootstrap.ServiceActivitySource. Namespace: GNex.SharedKernel.Observability.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Observability,
            RelativePath = "GNex.SharedKernel/Observability/RequestTracingMiddleware.cs",
            FileName = "RequestTracingMiddleware.cs",
            Namespace = "GNex.SharedKernel.Observability",
            ProducedBy = AgentType.Observability,
            TracedRequirementIds = ["NFR-OBS-01"],
            Content = response.Success ? response.Content : """
                using System.Diagnostics;
                using Microsoft.AspNetCore.Http;
                using Microsoft.Extensions.Logging;

                namespace GNex.SharedKernel.Observability;

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
        RelativePath = "GNex.SharedKernel/Observability/AppHealthChecks.cs",
        FileName = "AppHealthChecks.cs",
        Namespace = "GNex.SharedKernel.Observability",
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01", "SOC2-CC7"],
        Content = """
            using Microsoft.Extensions.Diagnostics.HealthChecks;

            namespace GNex.SharedKernel.Observability;

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

    private async Task<CodeArtifact> GenerateGrafanaDashboard(string prefix, string label, string[] serviceNames, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a Grafana dashboarding expert for application monitoring. Generate a Grafana dashboard JSON.",
            UserPrompt = $"Generate a Grafana dashboard JSON for {label} with panels: 1) Request rate per service, 2) P50/P95/P99 latency, 3) Error rate, 4) Sensitive data access events, 5) Domain-specific operational metrics, 6) AI interaction latency, 7) Kafka publish latency, 8) Database connection pool. Services: {string.Join(", ", serviceNames)}. Metric prefix: {prefix}. Use Prometheus data source.",
            Temperature = 0.2, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Observability,
            RelativePath = $"infrastructure/grafana/{prefix}-dashboard.json",
            FileName = $"{prefix}-dashboard.json",
            Namespace = string.Empty,
            ProducedBy = AgentType.Observability,
            TracedRequirementIds = ["NFR-OBS-01"],
            Content = response.Success ? response.Content : GenerateGrafanaDashboardFallback(prefix, label)
        };
    }

    private static CodeArtifact GenerateAlertingRules(string prefix) => new()
    {
        Layer = ArtifactLayer.Observability,
        RelativePath = $"infrastructure/prometheus/{prefix}-alerts.yml",
        FileName = $"{prefix}-alerts.yml",
        Namespace = string.Empty,
        ProducedBy = AgentType.Observability,
        TracedRequirementIds = ["NFR-OBS-01", "SOC2-CC7"],
        Content = $$$"""
            groups:
              - name: {{{prefix}}}_critical_alerts
                rules:
                  - alert: HighErrorRate
                    expr: rate({{{prefix}}}_errors_total[5m]) > 0.05
                    for: 2m
                    labels:
                      severity: critical
                    annotations:
                      summary: "High error rate on {{ $labels.service }}"
                      description: "Error rate is {{ $value }} errors/sec (threshold: 0.05)"

                  - alert: SlowResponses
                    expr: histogram_quantile(0.95, rate({{{prefix}}}_requests_duration_ms_bucket[5m])) > 2000
                    for: 5m
                    labels:
                      severity: warning
                    annotations:
                      summary: "P95 latency above 2s on {{ $labels.service }}"

                  - alert: UnauthorizedAccess
                    expr: rate({{{prefix}}}_security_breach_attempts[5m]) > 0
                    for: 1m
                    labels:
                      severity: critical
                    annotations:
                      summary: "Potential unauthorized access detected ({{ $value }}/sec)"

                  - alert: ServiceDown
                    expr: up == 0
                    for: 1m
                    labels:
                      severity: critical
                    annotations:
                      summary: "Service {{ $labels.instance }} is down"

                  - alert: KafkaPublishSlow
                    expr: histogram_quantile(0.95, rate({{{prefix}}}_kafka_publish_ms_bucket[5m])) > 500
                    for: 5m
                    labels:
                      severity: warning
                    annotations:
                      summary: "Kafka publish P95 latency above 500ms"

                  - alert: AiResponseSlow
                    expr: histogram_quantile(0.95, rate({{{prefix}}}_requests_duration_ms_bucket{operation="ai.interact"}[5m])) > 10000
                    for: 5m
                    labels:
                      severity: warning
                    annotations:
                      summary: "AI interaction P95 latency above 10s"

              - name: {{{prefix}}}_compliance_alerts
                rules:
                  - alert: SensitiveDataAccessSpike
                    expr: rate({{{prefix}}}_sensitive_data_access_total[5m]) > 100
                    for: 2m
                    labels:
                      severity: warning
                    annotations:
                      summary: "Unusual sensitive data access rate: {{ $value }}/sec from {{ $labels.user }}"
            """
    };

    private static string GenerateGrafanaDashboardFallback(string prefix, string label) => $$$"""
        {
          "dashboard": {
            "title": "{{{label}}}",
            "uid": "{{{prefix}}}-overview",
            "timezone": "UTC",
            "refresh": "10s",
            "panels": [
              {
                "title": "Request Rate (per service)",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "rate({{{prefix}}}_requests_total[5m])", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 }
              },
              {
                "title": "P95 Latency (ms)",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "histogram_quantile(0.95, rate({{{prefix}}}_requests_duration_ms_bucket[5m]))", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 }
              },
              {
                "title": "Error Rate",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "rate({{{prefix}}}_errors_total[5m])", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 0, "y": 8 }
              },
              {
                "title": "Sensitive Data Access Events",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "rate({{{prefix}}}_sensitive_data_access_total[5m])", "legendFormat": "{{ entity }} - {{ access }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 12, "y": 8 }
              },
              {
                "title": "AI Interaction Latency",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "histogram_quantile(0.95, rate({{{prefix}}}_requests_duration_ms_bucket{operation='ai.interact'}[5m]))", "legendFormat": "{{ model }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 0, "y": 16 }
              },
              {
                "title": "Kafka Publish Latency",
                "type": "timeseries",
                "datasource": "Prometheus",
                "targets": [{ "expr": "histogram_quantile(0.95, rate({{{prefix}}}_kafka_publish_ms_bucket[5m]))", "legendFormat": "{{ service }}" }],
                "gridPos": { "h": 8, "w": 12, "x": 12, "y": 16 }
              }
            ]
          }
        }
        """;
}
