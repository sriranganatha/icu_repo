using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Configuration;

/// <summary>
/// Configuration agent — generates per-environment appsettings, secrets management,
/// feature flags, CORS policies, rate limiting, and health-check configurations
/// for all 9 HMS microservices across Development, Staging, and Production.
/// </summary>
public sealed class ConfigurationAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<ConfigurationAgent> _logger;

    public AgentType Type => AgentType.Configuration;
    public string Name => "Configuration Agent";
    public string Description => "Generates per-environment configs, secrets management, feature flags, and service discovery.";

    private static readonly string[] Environments = ["Development", "Staging", "Production"];
    private static readonly (string Name, string Short, int Port)[] Services =
    [
        ("PatientService", "patient", 5101), ("EncounterService", "encounter", 5102),
        ("InpatientService", "inpatient", 5103), ("EmergencyService", "emergency", 5104),
        ("DiagnosticsService", "diagnostics", 5105), ("RevenueService", "revenue", 5106),
        ("AuditService", "audit", 5107), ("AiService", "ai", 5108),
        ("ApiGateway", "gateway", 5100),
    ];

    public ConfigurationAgent(ILlmProvider llm, ILogger<ConfigurationAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ConfigurationAgent starting");

        var artifacts = new List<CodeArtifact>();

        try
        {
            // ── Per-service, per-environment appsettings ──
            foreach (var (name, shortName, port) in Services)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var env in Environments)
                {
                    var prompt = $"""
                        Generate an appsettings.{env}.json for a .NET 8 microservice "{name}" (port {port})
                        in a Hospital Management System.
                        
                        Include:
                        - ConnectionStrings (PostgreSQL, Redis)
                        - Logging levels (appropriate for {env})
                        - JWT authentication settings
                        - Kafka messaging endpoints
                        - Health check endpoints
                        - CORS policy (appropriate for {env})
                        - Rate limiting (appropriate for {env})
                        - Feature flags section
                        - OpenTelemetry/tracing settings
                        - Secrets reference placeholders for {env} (use Azure Key Vault pattern for Production)
                        
                        Return ONLY valid JSON, no comments or explanations.
                        Use environment-appropriate values (e.g., stricter logging in Production, verbose in Development).
                        """;

                    var config = await _llm.GenerateAsync(prompt, ct);
                    config = config.Replace("```json", "").Replace("```", "").Trim();

                    artifacts.Add(new CodeArtifact
                    {
                        Layer = ArtifactLayer.Configuration,
                        RelativePath = $"src/Hms.{name}/appsettings.{env}.json",
                        FileName = $"appsettings.{env}.json",
                        Namespace = string.Empty,
                        ProducedBy = Type,
                        TracedRequirementIds = ["NFR-CONFIG-01"],
                        Content = config
                    });
                }
            }

            // ── Feature flags configuration ──
            var ffPrompt = $"""
                Generate a feature-flags.json for a Hospital Management System with 9 microservices.
                Include flags for:
                - NewPatientWorkflow, EnhancedTriage, AiCopilot, BulkImport, RealTimeAlerts
                - FhirR4Support, HipaaEnhancedAudit, DarkMode, MaintenanceMode
                Each flag has: name, description, enabled (bool), rolloutPercentage (0-100), environments (array)
                Return ONLY valid JSON.
                """;
            var ffContent = await _llm.GenerateAsync(ffPrompt, ct);
            ffContent = ffContent.Replace("```json", "").Replace("```", "").Trim();
            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Configuration,
                RelativePath = "config/feature-flags.json",
                FileName = "feature-flags.json",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-CONFIG-02"],
                Content = ffContent
            });

            // ── Secrets management template ──
            artifacts.Add(GenerateSecretsTemplate());

            // ── Service discovery / Consul config ──
            artifacts.Add(GenerateServiceDiscovery());

            // ── .env template files ──
            foreach (var env in Environments)
            {
                artifacts.Add(GenerateEnvFile(env));
            }

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Configuration Agent: {artifacts.Count} config artifacts — {Services.Length} services × {Environments.Length} environments + feature flags + secrets",
                Artifacts = artifacts, Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ConfigurationAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateSecretsTemplate() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "config/secrets-template.json",
        FileName = "secrets-template.json",
        Namespace = string.Empty,
        ProducedBy = AgentType.Configuration,
        TracedRequirementIds = ["NFR-CONFIG-03"],
        Content = """
            {
              "_comment": "Template for secrets — never commit actual values. Use Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault.",
              "DatabasePassword": "${DB_PASSWORD}",
              "JwtSigningKey": "${JWT_SIGNING_KEY}",
              "KafkaSaslPassword": "${KAFKA_SASL_PASSWORD}",
              "RedisPassword": "${REDIS_PASSWORD}",
              "SmtpPassword": "${SMTP_PASSWORD}",
              "AiApiKey": "${AI_API_KEY}",
              "FhirClientSecret": "${FHIR_CLIENT_SECRET}",
              "EncryptionKey": "${ENCRYPTION_KEY_BASE64}",
              "HipaaAuditSigningKey": "${HIPAA_AUDIT_KEY}"
            }
            """
    };

    private static CodeArtifact GenerateServiceDiscovery() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "config/service-discovery.json",
        FileName = "service-discovery.json",
        Namespace = string.Empty,
        ProducedBy = AgentType.Configuration,
        TracedRequirementIds = ["NFR-CONFIG-04"],
        Content = System.Text.Json.JsonSerializer.Serialize(new
        {
            services = Services.Select(s => new
            {
                name = s.Name,
                shortName = s.Short,
                port = s.Port,
                healthEndpoint = $"http://localhost:{s.Port}/healthz",
                baseUrl = $"http://localhost:{s.Port}",
                grpcPort = s.Port + 1000
            })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
    };

    private static CodeArtifact GenerateEnvFile(string env)
    {
        var isProd = env == "Production";
        return new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration,
            RelativePath = $"config/.env.{env.ToLowerInvariant()}",
            FileName = $".env.{env.ToLowerInvariant()}",
            Namespace = string.Empty,
            ProducedBy = AgentType.Configuration,
            TracedRequirementIds = ["NFR-CONFIG-05"],
            Content = $"""
                # HMS Environment: {env}
                ASPNETCORE_ENVIRONMENT={env}
                DB_HOST={(isProd ? "hms-db.internal" : "localhost")}
                DB_PORT=5432
                DB_NAME=hms
                DB_USER=hms
                DB_PASSWORD=$DB_PASSWORD_PLACEHOLDER
                REDIS_HOST={(isProd ? "hms-redis.internal" : "localhost")}
                REDIS_PORT=6379
                KAFKA_BROKERS={(isProd ? "hms-kafka-1.internal:9092,hms-kafka-2.internal:9092" : "localhost:9092")}
                JWT_ISSUER=hms-{env.ToLowerInvariant()}
                JWT_AUDIENCE=hms-api
                LOG_LEVEL={(isProd ? "Warning" : env == "Staging" ? "Information" : "Debug")}
                OTEL_EXPORTER_ENDPOINT={(isProd ? "https://otel.hms.internal:4317" : "http://localhost:4317")}
                ENABLE_SWAGGER={(isProd ? "false" : "true")}
                """
        };
    }
}
