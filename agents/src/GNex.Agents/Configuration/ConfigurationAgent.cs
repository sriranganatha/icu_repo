using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Extensions;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Configuration;

/// <summary>
/// Configuration agent — generates per-environment appsettings, secrets management,
/// feature flags, CORS policies, rate limiting, and health-check configurations
/// for all derived microservices across Development, Staging, and Production.
/// </summary>
public sealed class ConfigurationAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<ConfigurationAgent> _logger;

    public AgentType Type => AgentType.Configuration;
    public string Name => "Configuration Agent";
    public string Description => "Generates per-environment configs, secrets management, feature flags, and service discovery.";

    private static readonly string[] Environments = ["Development", "Staging", "Production"];

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
        var services = ServiceCatalogResolver.GetServices(context);
        var solutionNs = context.PipelineConfig?.SolutionNamespace ?? "GNex";

        try
        {
            // ── Read feedback from previous iterations ──
            var feedback = context.ReadFeedback(Type);
            if (feedback.Count > 0)
            {
                _logger.LogInformation("ConfigurationAgent received {Count} feedback items", feedback.Count);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Incorporating {feedback.Count} feedback items from previous iterations");
            }

            // ── Use DomainProfile for domain-aware config generation ──
            var profile = context.DomainProfile;
            if (profile is not null)
            {
                _logger.LogInformation("ConfigurationAgent using DomainProfile: {Domain}, {ComplianceCount} compliance frameworks",
                    profile.Domain, profile.ComplianceFrameworks?.Count ?? 0);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"DomainProfile active: {profile.Domain} — configuring {profile.ComplianceFrameworks?.Count ?? 0} compliance frameworks, {profile.SensitiveFieldPatterns?.Count ?? 0} sensitive data rules");
            }

            // ── Per-service, per-environment appsettings ──
            var llmContext = context.BuildLlmContextBlock(Type);

            foreach (var svc in services)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var env in Environments)
                {
                    var configPrompt = new LlmPrompt
                    {
                        SystemPrompt = $$"""
                            You are a senior .NET 8 DevOps engineer generating per-environment configuration.
                            Generate production-quality appsettings JSON for microservices.
                            Return ONLY valid JSON, no comments or explanations.

                            {{(!string.IsNullOrWhiteSpace(llmContext) ? llmContext : "")}}
                            """,
                        UserPrompt = $"""
                            Generate an appsettings.{env}.json for a .NET 8 microservice "{svc.Name}" (port {svc.ApiPort})
                            in a microservices platform. Service description: {svc.Description}
                            
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

                            Use environment-appropriate values (e.g., stricter logging in Production, verbose in Development).
                            """,
                        Temperature = 0.3,
                        MaxTokens = 3000,
                        RequestingAgent = "ConfigurationAgent"
                    };

                    var response = await _llm.GenerateAsync(configPrompt, ct);
                    var config = response.Success ? response.Content ?? "{}" : "{}";
                    config = config.Replace("```json", "").Replace("```", "").Trim();

                    artifacts.Add(new CodeArtifact
                    {
                        Layer = ArtifactLayer.Configuration,
                        RelativePath = $"src/{solutionNs}.{svc.Name}/appsettings.{env}.json",
                        FileName = $"appsettings.{env}.json",
                        Namespace = string.Empty,
                        ProducedBy = Type,
                        TracedRequirementIds = ["NFR-CONFIG-01"],
                        Content = config
                    });
                }
            }

            // ── Feature flags configuration ──
            var serviceNames = string.Join(", ", services.Select(s => s.Name));
            var ffPrompt = new LlmPrompt
            {
                SystemPrompt = $$"""
                    You are a senior .NET 8 DevOps engineer designing feature flag configurations.
                    Return ONLY valid JSON array of feature flag objects.

                    {{(!string.IsNullOrWhiteSpace(llmContext) ? llmContext : "")}}
                    """,
                UserPrompt = $"""
                    Generate a feature-flags.json for a microservices platform with these services: {serviceNames}.
                    Include flags for:
                    - DarkMode, MaintenanceMode, BulkImport, RealTimeAlerts, AiCopilot
                    - Per-service feature toggles based on service capabilities
                    Each flag has: name, description, enabled (bool), rolloutPercentage (0-100), environments (array)
                    """,
                Temperature = 0.3,
                MaxTokens = 2000,
                RequestingAgent = "ConfigurationAgent"
            };
            var ffResponse = await _llm.GenerateAsync(ffPrompt, ct);
            var ffContent = ffResponse.Success ? ffResponse.Content ?? "[]" : "[]";
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
            artifacts.Add(GenerateServiceDiscovery(services, solutionNs));

            // ── .env template files ──
            foreach (var env in Environments)
            {
                artifacts.Add(GenerateEnvFile(env, solutionNs));
            }

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Configuration Agent: {artifacts.Count} config artifacts — {services.Count} services × {Environments.Length} environments + feature flags + secrets",
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
              "IntegrationClientSecret": "${INTEGRATION_CLIENT_SECRET}",
              "EncryptionKey": "${ENCRYPTION_KEY_BASE64}",
              "AuditSigningKey": "${AUDIT_SIGNING_KEY}"
            }
            """
    };

    private static CodeArtifact GenerateServiceDiscovery(IReadOnlyList<MicroserviceDefinition> services, string solutionNs) => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "config/service-discovery.json",
        FileName = "service-discovery.json",
        Namespace = string.Empty,
        ProducedBy = AgentType.Configuration,
        TracedRequirementIds = ["NFR-CONFIG-04"],
        Content = System.Text.Json.JsonSerializer.Serialize(new
        {
            services = services.Select(s => new
            {
                name = s.Name,
                shortName = s.ShortName,
                port = s.ApiPort,
                healthEndpoint = $"http://localhost:{s.ApiPort}/healthz",
                baseUrl = $"http://localhost:{s.ApiPort}",
                grpcPort = s.ApiPort + 1000
            })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
    };

    private static CodeArtifact GenerateEnvFile(string env, string solutionNs)
    {
        var isProd = env == "Production";
        var lower = solutionNs.ToLowerInvariant();
        return new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration,
            RelativePath = $"config/.env.{env.ToLowerInvariant()}",
            FileName = $".env.{env.ToLowerInvariant()}",
            Namespace = string.Empty,
            ProducedBy = AgentType.Configuration,
            TracedRequirementIds = ["NFR-CONFIG-05"],
            Content = $"""
                # {solutionNs} Environment: {env}
                ASPNETCORE_ENVIRONMENT={env}
                DB_HOST={(isProd ? $"{lower}-db.internal" : "localhost")}
                DB_PORT=5432
                DB_NAME={lower}
                DB_USER={lower}_admin
                DB_PASSWORD=$DB_PASSWORD_PLACEHOLDER
                REDIS_HOST={(isProd ? $"{lower}-redis.internal" : "localhost")}
                REDIS_PORT=6379
                KAFKA_BROKERS={(isProd ? $"{lower}-kafka-1.internal:9092,{lower}-kafka-2.internal:9092" : "localhost:9092")}
                JWT_ISSUER={lower}-{env.ToLowerInvariant()}
                JWT_AUDIENCE={lower}-api
                LOG_LEVEL={(isProd ? "Warning" : env == "Staging" ? "Information" : "Debug")}
                OTEL_EXPORTER_ENDPOINT={(isProd ? $"https://otel.{lower}.internal:4317" : "http://localhost:4317")}
                ENABLE_SWAGGER={(isProd ? "false" : "true")}
                """
        };
    }
}
