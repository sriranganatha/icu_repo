using System.Diagnostics;
using System.Text.RegularExpressions;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Database;

/// <summary>
/// Generates database artifacts per microservice: each bounded context gets its own
/// DbContext, entities, repositories, and migration scripts — aligned to schema-per-service.
/// Also provisions Docker PostgreSQL and executes DDL when configured.
/// </summary>
public sealed class DatabaseAgent : IAgent
{
    private readonly ILogger<DatabaseAgent> _logger;
    private readonly ILlmProvider _llm;
    // Track which artifact paths this agent has already produced in this pipeline run
    private readonly HashSet<string> _generatedPaths = new(StringComparer.OrdinalIgnoreCase);
    // Track whether DDL has been executed this pipeline run
    private bool _ddlExecutedThisRun;

    public AgentType Type => AgentType.Database;
    public string Name => "Database Agent";
    public string Description => "Generates per-microservice EF Core entities, DbContext, repositories, migrations, and provisions Docker PostgreSQL.";

    public DatabaseAgent(ILogger<DatabaseAgent> logger, ILlmProvider llm) { _logger = logger; _llm = llm; }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        var guidance = GetGuidanceSummary(context);

        // ── Resolve which services need work based on assigned backlog items ──
        var assignedItems = context.ExpandedRequirements
            .Where(i => i.Status == WorkItemStatus.InProgress
                     && i.AssignedAgent.Contains("Database", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var scopedServices = assignedItems.Count > 0
            ? ResolveServicesFromBacklogItems(assignedItems, context)
            : ResolveTargetServices(context);   // first run: no items claimed yet → generate all

        // ── Filter out services whose artifacts were already generated ──
        var newServices = scopedServices
            .Where(svc => !_generatedPaths.Contains($"{svc.ProjectName}/Data/{svc.DbContextName}.cs"))
            .ToList();

        if (newServices.Count == 0 && _ddlExecutedThisRun)
        {
            _logger.LogInformation("DatabaseAgent skipping — all {Count} services already generated and DDL already executed",
                scopedServices.Count);
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Database layer up-to-date — {scopedServices.Count} services already generated, DDL already executed. Nothing to do.");

            // Mark assigned items as completed even on skip
            MarkItemsCompleted(context);

            context.AgentStatuses[Type] = AgentStatus.Completed;
            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Database layer up-to-date — {scopedServices.Count} services already generated. Skipped re-execution.",
                Duration = sw.Elapsed
            };
        }

        _logger.LogInformation("DatabaseAgent starting — {New} new services to generate (of {Total} total), {Items} backlog items assigned",
            newServices.Count, scopedServices.Count, assignedItems.Count);

        var artifacts = new List<CodeArtifact>();
        var messages = new List<AgentMessage>();

        try
        {
            if (context.ReportProgress is not null && !string.IsNullOrWhiteSpace(guidance))
                await context.ReportProgress(Type, $"Applying architecture/platform guidance: {guidance}");

            // Read feedback from downstream agents (Review, Supervisor, GapAnalysis)
            var feedback = context.ReadFeedback(Type);
            if (feedback.Count > 0)
                _logger.LogInformation("DatabaseAgent received {Count} feedback items", feedback.Count);

            // Read historical learnings from previous pipeline runs
            var learnings = context.GetLearningsForAgent(Type);
            if (learnings.Count > 0)
            {
                _logger.LogInformation("DatabaseAgent loaded {Count} historical learnings", learnings.Count);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Applying {learnings.Count} historical learnings to avoid past issues");
            }

            // Generate per-service database artifacts (only for NEW services)
            foreach (var svc in newServices)
            {
                _logger.LogInformation("Generating DB layer for {Service} ({Schema})", svc.Name, svc.Schema);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Generating DB layer for {svc.Name} — schema: {svc.Schema}, entities: {string.Join(", ", svc.Entities)}");

                // LLM-driven entity generation: one batch call per service
                var entityBodies = await GenerateServiceEntitiesViaLlmAsync(svc, context, ct);
                foreach (var entity in svc.Entities)
                {
                    var body = entityBodies.GetValueOrDefault(entity) ?? GenerateDefaultEntity(svc, entity);
                    AddIfNew(artifacts, new CodeArtifact
                    {
                        Layer = ArtifactLayer.Database,
                        RelativePath = $"{svc.ProjectName}/Data/Entities/{entity}.cs",
                        FileName = $"{entity}.cs",
                        Namespace = $"{svc.Namespace}.Data.Entities",
                        ProducedBy = AgentType.Database,
                        Content = body
                    });
                }

                AddIfNew(artifacts, GenerateDbContext(svc));

                foreach (var entity in svc.Entities)
                    AddIfNew(artifacts, GenerateRepository(svc, entity));

                AddIfNew(artifacts, await GenerateMigrationScriptAsync(svc, context, ct));

                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"{svc.Name}: Generated {svc.Entities.Length} entities, DbContext, {svc.Entities.Length} repositories, migration script");
            }

            // Shared artifacts (only if we generated new services)
            if (newServices.Count > 0)
            {
                AddIfNew(artifacts, GenerateRlsMigration(scopedServices));
                AddIfNew(artifacts, GenerateDockerCompose(context.PipelineConfig, scopedServices));
            }

            context.Artifacts.AddRange(artifacts);

            // Docker + DDL provisioning (only run DDL once per pipeline)
            var config = context.PipelineConfig;
            var ddlExecuted = false;
            var ddlSuccess = true;
            if (config is not null && !_ddlExecutedThisRun)
            {
                var provisioner = new DockerDbProvisioner(_logger);
                var containerOk = true; // assume ok if not spinning up

                if (config.SpinUpDocker)
                {
                    _logger.LogInformation("Spinning up Docker PostgreSQL container...");
                    if (context.ReportProgress is not null)
                        await context.ReportProgress(Type, $"Spinning up Docker PostgreSQL — container: {config.DockerContainerName}, port: {config.DbPort}");
                    containerOk = await provisioner.EnsureContainerAsync(config, ct);
                    messages.Add(new AgentMessage
                    {
                        From = Type, To = AgentType.Orchestrator,
                        Subject = containerOk ? "Docker container ready" : "Docker container failed",
                        Body = containerOk
                            ? $"PostgreSQL container '{config.DockerContainerName}' running on port {config.DbPort}"
                            : "Could not start PostgreSQL container. DDL execution skipped."
                    });

                    if (!containerOk)
                    {
                        ddlSuccess = false;
                        context.Findings.Add(new ReviewFinding
                        {
                            Category = "Deployment",
                            Severity = ReviewSeverity.Critical,
                            Message = $"Docker PostgreSQL container '{config.DockerContainerName}' failed to start. DDL execution was skipped.",
                            FilePath = "Infrastructure/Docker",
                            Suggestion = "Verify Docker is running, check container name and port conflicts."
                        });
                        if (context.ReportProgress is not null)
                            await context.ReportProgress(Type, $"⚠ Docker container '{config.DockerContainerName}' failed — DDL skipped");
                    }
                }

                // Execute DDL — decoupled from SpinUpDocker so DDL runs against existing containers too
                if (containerOk && config.ExecuteDdl)
                {
                    _logger.LogInformation("Executing DDL — per-service schemas, tables, indexes, stored procedures, RLS...");
                    if (context.ReportProgress is not null)
                        await context.ReportProgress(Type, "Executing DDL — creating schemas, tables, indexes, stored procedures, RLS policies...");
                    var (ddlOk, objectsCreated, ddlErrors) = await provisioner.ExecuteDdlAsync(config, ct);
                    ddlExecuted = true;
                    ddlSuccess = ddlOk;
                    _ddlExecutedThisRun = ddlOk; // Only mark as done if successful
                    if (context.ReportProgress is not null)
                        await context.ReportProgress(Type, $"DDL execution: {objectsCreated} database objects created{(ddlErrors.Count > 0 ? $", {ddlErrors.Count} errors" : "")}");

                    // Surface DDL errors as ReviewFindings so orchestrator can dispatch BugFix
                    foreach (var ddlErr in ddlErrors)
                    {
                        context.Findings.Add(new ReviewFinding
                        {
                            Category = "Database",
                            Severity = ReviewSeverity.Error,
                            Message = $"DDL execution error: {ddlErr}",
                            FilePath = "Infrastructure/Migrations/DDL",
                            Suggestion = "Check PostgreSQL connectivity, verify schema/table DDL syntax, and ensure the database user has CREATE privileges."
                        });
                        _logger.LogWarning("DDL error surfaced as finding: {Error}", ddlErr);
                        if (context.ReportProgress is not null)
                            await context.ReportProgress(Type, $"⚠ DDL error: {ddlErr}");
                    }

                    messages.Add(new AgentMessage
                    {
                        From = Type, To = AgentType.Orchestrator,
                        Subject = ddlOk ? "DDL execution succeeded" : "DDL completed with errors",
                        Body = $"Created {objectsCreated} database objects." +
                               (ddlErrors.Count > 0 ? $" Errors: {string.Join("; ", ddlErrors)}" : "")
                    });

                    artifacts.Add(new CodeArtifact
                    {
                        Layer = ArtifactLayer.Migration,
                        RelativePath = "Infrastructure/Migrations/DdlExecutionLog.txt",
                        FileName = "DdlExecutionLog.txt",
                        Namespace = "GNex.Infrastructure",
                        ProducedBy = AgentType.Database,
                        Content = $"Objects Created: {objectsCreated} | Errors: {ddlErrors.Count}\n" +
                                  string.Join("\n", ddlErrors.Select(e => $"ERROR: {e}"))
                    });
                }
            }

            // Artifact generation is the primary success criterion.
            // DDL errors are surfaced as findings for downstream agents to fix,
            // but should NOT fail the entire agent when code artifacts are generated.
            var artifactsGenerated = artifacts.Count > 0 || _generatedPaths.Count > 0;
            var agentSuccess = artifactsGenerated;
            context.AgentStatuses[Type] = agentSuccess ? AgentStatus.Completed : AgentStatus.Failed;
            var ddlSuffix = ddlExecuted ? (ddlSuccess ? " | DDL executed successfully" : " | DDL completed with errors (non-blocking)") : "";
            if (!ddlSuccess && artifactsGenerated)
                _logger.LogWarning("DatabaseAgent: DDL had errors but artifacts were generated successfully — marking agent as Completed");

            // Mark assigned backlog items as completed
            MarkItemsCompleted(context);

            _logger.LogInformation("DatabaseAgent completed — {New} new artifacts, {Total} total generated across {Svc} microservices{Ddl}",
                artifacts.Count, _generatedPaths.Count, scopedServices.Count, ddlSuffix);

            return new AgentResult
            {
                Agent = Type,
                Success = agentSuccess,
                Summary = $"Generated {artifacts.Count} new DB artifacts ({newServices.Count} new services of {scopedServices.Count} total){ddlSuffix}",
                Artifacts = artifacts,
                Messages =
                [
                    ..messages,
                    new AgentMessage
                    {
                        From = Type, To = AgentType.Orchestrator,
                        Subject = "Database layer ready",
                        Body = $"{artifacts.Count} artifacts: per-service DbContexts, entities, repos, migrations. Scoped services: {string.Join(", ", scopedServices.Select(s => s.Name))}."
                    }
                ],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "DatabaseAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    // ─── Per-Service DbContext ───────────────────────────────────────────────

    private static CodeArtifact GenerateDbContext(MicroserviceDefinition svc)
    {
        var dbSets = string.Join("\n        ",
            svc.Entities.Select(e => $"public DbSet<{e}> {e}s => Set<{e}>();"));

        var tableMappings = string.Join("\n            ",
            svc.Entities.Select(e => $"modelBuilder.Entity<{e}>().ToTable(\"{ToSnakeCase(e)}\", \"{svc.Schema}\");"));

        var tenantFilters = string.Join("\n            ",
            svc.Entities.Select(e => $"modelBuilder.Entity<{e}>().HasQueryFilter(x => x.TenantId == _tenantId);"));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Database,
            RelativePath = $"{svc.ProjectName}/Data/{svc.DbContextName}.cs",
            FileName = $"{svc.DbContextName}.cs",
            Namespace = $"{svc.Namespace}.Data",
            ProducedBy = AgentType.Database,
            Content = $$"""
                using Microsoft.EntityFrameworkCore;
                using {{svc.Namespace}}.Data.Entities;

                namespace {{svc.Namespace}}.Data;

                public class {{svc.DbContextName}} : DbContext
                {
                    private readonly string _tenantId;

                    public {{svc.DbContextName}}(DbContextOptions<{{svc.DbContextName}}> options, ITenantProvider tenant)
                        : base(options) => _tenantId = tenant.TenantId;

                    {{dbSets}}

                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        base.OnModelCreating(modelBuilder);

                        // Schema: {{svc.Schema}}
                        {{tableMappings}}

                        // Tenant isolation query filters
                        {{tenantFilters}}
                    }
                }

                public interface ITenantProvider { string TenantId { get; } }
                """
        };
    }

    // ─── Per-Service Repository ─────────────────────────────────────────────

    private static CodeArtifact GenerateRepository(MicroserviceDefinition svc, string entity)
    {
        var repoName = $"{entity}Repository";
        return new CodeArtifact
        {
            Layer = ArtifactLayer.Repository,
            RelativePath = $"{svc.ProjectName}/Data/Repositories/{repoName}.cs",
            FileName = $"{repoName}.cs",
            Namespace = $"{svc.Namespace}.Data.Repositories",
            ProducedBy = AgentType.Database,
            Content = $$"""
                using Microsoft.EntityFrameworkCore;
                using {{svc.Namespace}}.Data;
                using {{svc.Namespace}}.Data.Entities;

                namespace {{svc.Namespace}}.Data.Repositories;

                public interface I{{repoName}}
                {
                    Task<{{entity}}?> GetByIdAsync(string id, CancellationToken ct = default);
                    Task<List<{{entity}}>> ListAsync(int skip, int take, CancellationToken ct = default);
                    Task<{{entity}}> CreateAsync({{entity}} entity, CancellationToken ct = default);
                    Task UpdateAsync({{entity}} entity, CancellationToken ct = default);
                }

                public class {{repoName}} : I{{repoName}}
                {
                    private readonly {{svc.DbContextName}} _db;
                    public {{repoName}}({{svc.DbContextName}} db) => _db = db;

                    public async Task<{{entity}}?> GetByIdAsync(string id, CancellationToken ct = default)
                        => await _db.Set<{{entity}}>().FindAsync([id], ct);

                    public async Task<List<{{entity}}>> ListAsync(int skip, int take, CancellationToken ct = default)
                        => await _db.Set<{{entity}}>().OrderByDescending(e => e.CreatedAt)
                            .Skip(skip).Take(take).ToListAsync(ct);

                    public async Task<{{entity}}> CreateAsync({{entity}} entity, CancellationToken ct = default)
                    {
                        _db.Set<{{entity}}>().Add(entity);
                        await _db.SaveChangesAsync(ct);
                        return entity;
                    }

                    public async Task UpdateAsync({{entity}} entity, CancellationToken ct = default)
                    {
                        _db.Set<{{entity}}>().Update(entity);
                        await _db.SaveChangesAsync(ct);
                    }
                }
                """
        };
    }

    // ─── Shared RLS Migration ───────────────────────────────────────────────

    private static CodeArtifact GenerateRlsMigration(IEnumerable<MicroserviceDefinition> services)
    {
        var policies = string.Join("\n\n", services.SelectMany(svc =>
            svc.Entities.Select(e =>
            {
                var table = $"{svc.Schema}.{ToSnakeCase(e)}";
                var policy = $"tenant_isolation_{ToSnakeCase(e)}";
                return $"""
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS {policy} ON {table};
                    CREATE POLICY {policy} ON {table}
                        USING (tenant_id = current_setting('app.current_tenant_id', true));
                    """;
            })));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Migration,
            RelativePath = "Infrastructure/Migrations/V2__rls_all_services.sql",
            FileName = "V2__rls_all_services.sql",
            Namespace = "GNex.Infrastructure",
            ProducedBy = AgentType.Database,
            Content = $"""
                -- Row-Level Security policies for all microservices
                -- Ensures tenant isolation at the database level

                {policies}
                """
        };
    }

        // ─── Docker Compose ─────────────────────────────────────────────────────

        private static CodeArtifact GenerateDockerCompose(PipelineConfig? config, IEnumerable<MicroserviceDefinition> services)
        {
                var dbUser = config?.DbUser ?? "gnex_admin";
                var dbPassword = config?.DbPassword ?? "gnex_dev_pw";
                var dbName = config?.DbName ?? "app_db";
                var dbPort = config?.DbPort ?? 5432;
                var serviceList = services.ToList();

                var svcEntries = string.Join("\n\n", serviceList.Select(svc => $$"""
                            {{svc.ShortName}}-api:
                                build:
                                    context: .
                                    dockerfile: {{svc.ProjectName}}/Dockerfile
                                ports:
                                    - "{{svc.ApiPort}}:8080"
                                environment:
                                    - ConnectionStrings__Default=Host=postgres;Port=5432;Database={{dbName}};Username={{dbUser}};Password=${DB_PASSWORD:-{{dbPassword}}}
                                    - Kafka__BootstrapServers=kafka:9092
                                    - TenantId=default
                                depends_on:
                                    postgres:
                                        condition: service_healthy
                                    kafka:
                                        condition: service_healthy
                        """));

                var gatewayDepends = string.Join("\n", serviceList.Select(svc => $"                      - {svc.ShortName}-api"));

                return new CodeArtifact
                {
                        Layer = ArtifactLayer.Configuration,
                        RelativePath = "docker-compose.yml",
                        FileName = "docker-compose.yml",
                        Namespace = "GNex.Infrastructure",
                        ProducedBy = AgentType.Database,
                        Content = $$"""
                                version: '3.9'
                                services:
                                    postgres:
                                        image: postgres:16-alpine
                                        ports:
                                            - "{{dbPort}}:5432"
                                        environment:
                                            POSTGRES_USER: {{dbUser}}
                                            POSTGRES_PASSWORD: ${DB_PASSWORD:-{{dbPassword}}}
                                            POSTGRES_DB: {{dbName}}
                                        volumes:
                                            - pgdata:/var/lib/postgresql/data
                                        healthcheck:
                                            test: ["CMD-SHELL", "pg_isready -U {{dbUser}}"]
                                            interval: 5s
                                            timeout: 5s
                                            retries: 10

                                    kafka:
                                        image: bitnami/kafka:3.7
                                        ports:
                                            - "9092:9092"
                                        environment:
                                            KAFKA_CFG_NODE_ID: 0
                                            KAFKA_CFG_PROCESS_ROLES: controller,broker
                                            KAFKA_CFG_LISTENERS: PLAINTEXT://:9092,CONTROLLER://:9093
                                            KAFKA_CFG_LISTENER_SECURITY_PROTOCOL_MAP: CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT
                                            KAFKA_CFG_CONTROLLER_QUORUM_VOTERS: 0@kafka:9093
                                            KAFKA_CFG_CONTROLLER_LISTENER_NAMES: CONTROLLER
                                            KAFKA_CFG_AUTO_CREATE_TOPICS_ENABLE: "true"
                                        volumes:
                                            - kafkadata:/bitnami/kafka
                                        healthcheck:
                                            test: ["CMD-SHELL", "kafka-broker-api-versions.sh --bootstrap-server localhost:9092"]
                                            interval: 10s
                                            timeout: 10s
                                            retries: 10

                                {{svcEntries}}

                                    api-gateway:
                                        build:
                                            context: .
                                            dockerfile: GNex.ApiGateway/Dockerfile
                                        ports:
                                            - "5100:8080"
                                        environment:
                                            - Kafka__BootstrapServers=kafka:9092
                                        depends_on:
                                {{gatewayDepends}}

                                volumes:
                                    pgdata:
                                    kafkadata:
                                """
                };
        }

    private static List<MicroserviceDefinition> ResolveTargetServices(AgentContext context)
    {
        var catalog = ServiceCatalogResolver.GetServices(context);

        var archInstruction = context.OrchestratorInstructions
            .FirstOrDefault(i => i.StartsWith("[ARCH]", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(archInstruction))
            return catalog.ToList();

        var marker = "TARGET_SERVICES=";
        var start = archInstruction.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return catalog.ToList();

        start += marker.Length;
        var end = archInstruction.IndexOf(';', start);
        var csv = end >= 0 ? archInstruction[start..end] : archInstruction[start..];

        var resolved = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => ServiceCatalogResolver.ByName(context, name))
            .Where(s => s is not null)
            .Cast<MicroserviceDefinition>()
            .ToList();

        return resolved.Count > 0 ? resolved : catalog.ToList();
    }

    /// <summary>
    /// Resolve microservices from assigned backlog items by matching tags, module, and title keywords
    /// to services in the catalog. Falls back to full catalog if no matches found.
    /// </summary>
    private static List<MicroserviceDefinition> ResolveServicesFromBacklogItems(
        List<ExpandedRequirement> items, AgentContext context)
    {
        var catalog = ServiceCatalogResolver.GetServices(context);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var text = $"{item.Title} {item.Description} {item.Module} {string.Join(" ", item.Tags)}";

            foreach (var svc in catalog)
            {
                if (matched.Contains(svc.Name)) continue;

                // Match by schema name, service name, short name, or entity names
                if (text.Contains(svc.ShortName, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(svc.Schema, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(svc.Name, StringComparison.OrdinalIgnoreCase)
                    || svc.Entities.Any(e => text.Contains(e, StringComparison.OrdinalIgnoreCase)))
                {
                    matched.Add(svc.Name);
                }
            }
        }

        if (matched.Count == 0)
        {
            // Fallback: use arch instructions or full catalog
            return ResolveTargetServices(context);
        }

        return catalog.Where(s => matched.Contains(s.Name)).ToList();
    }

    /// <summary>
    /// Add artifact only if its path hasn't been generated before in this pipeline run.
    /// </summary>
    private void AddIfNew(List<CodeArtifact> artifacts, CodeArtifact artifact)
    {
        if (_generatedPaths.Add(artifact.RelativePath))
            artifacts.Add(artifact);
    }

    /// <summary>
    /// Complete all claimed items via the agent-owned lifecycle delegate.
    /// </summary>
    private static void MarkItemsCompleted(AgentContext context)
    {
        foreach (var item in context.CurrentClaimedItems)
            context.CompleteWorkItem?.Invoke(item);
    }

    private static string GetGuidanceSummary(AgentContext context)
    {
        var guidance = context.OrchestratorInstructions
            .Where(i => i.StartsWith("[ARCH]", StringComparison.OrdinalIgnoreCase)
                     || i.StartsWith("[PLATFORM]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return guidance.Count == 0 ? string.Empty : string.Join(" | ", guidance);
    }

    // ─── LLM-Driven Entity Generation ──────────────────────────────────────

    /// <summary>
    /// Generates all entity C# classes for a service via a single LLM call.
    /// Returns a dictionary mapping entity name → C# source code.
    /// Falls back gracefully — returns empty dict so callers use GenerateDefaultEntity.
    /// </summary>
    private async Task<Dictionary<string, string>> GenerateServiceEntitiesViaLlmAsync(
        MicroserviceDefinition svc, AgentContext context, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var requirementsSummary = string.Join("\n", context.Requirements
            .Take(80)
            .Select(r => $"- {r.Title}: {r.Description}"));

        // Domain-specific guidance from PromptGeneratorAgent
        var domainGuidance = "";
        var profile = context.DomainProfile;
        if (profile is not null)
        {
            var parts = new List<string>();
            if (profile.AgentPrompts.TryGetValue("Database", out var agentPrompt))
                parts.Add($"Domain Agent Guidance:\n{agentPrompt}");
            if (profile.DomainGlossary is { Count: > 0 })
                parts.Add("Domain Glossary (use these terms for field/entity naming):\n" +
                    string.Join("\n", profile.DomainGlossary.Take(20).Select(kv => $"  {kv.Key}: {kv.Value}")));
            if (profile.BusinessRules is { Count: > 0 })
                parts.Add("Business Rules (reflect as validation fields/constraints):\n" +
                    string.Join("\n", profile.BusinessRules.Take(15).Select(r => $"  • {r}")));
            if (profile.SensitiveFieldPatterns is { Count: > 0 })
                parts.Add("Sensitive Fields (mark with [PersonalData] attribute):\n" +
                    string.Join(", ", profile.SensitiveFieldPatterns.Take(20)));
            if (parts.Count > 0)
                domainGuidance = "\n\n=== DOMAIN CONTEXT ===\n" + string.Join("\n\n", parts);
        }

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a senior .NET developer generating EF Core entity classes.
                Generate one C# entity class per requested entity for a microservice.

                Every entity MUST have these standard fields:
                - [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                - [Required] public string TenantId { get; set; } = null!;
                - public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                - [Required] public string CreatedBy { get; set; } = null!;
                - public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
                - [Required] public string UpdatedBy { get; set; } = null!;
                - public int VersionNo { get; set; } = 1;

                Add domain-specific fields based on the entity name, service context, and
                domain glossary/business rules provided. Use domain terminology for field
                names — match the exact terms in the glossary when applicable.
                Use [Required] on non-nullable reference types. Use nullable types (?) for optional fields.
                Mark sensitive/PII fields with [PersonalData] when they match sensitive field patterns.
                Include navigation properties where parent-child relationships exist between entities in the SAME service.

                DELIMITER FORMAT: Separate each entity with a line containing exactly:
                // === EntityName.cs ===
                Do NOT use markdown code fences. Output raw C# only.
                """,
            UserPrompt = $"""
                Service: {svc.Name} (schema: {svc.Schema})
                Description: {svc.Description}
                Namespace: {svc.Namespace}.Data.Entities

                Generate entity classes for: {string.Join(", ", svc.Entities)}

                Project requirements context (use these to infer appropriate domain fields):
                {requirementsSummary}{domainGuidance}
                """,
            Temperature = 0.2,
            MaxTokens = 4096,
            RequestingAgent = "DatabaseAgent"
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("LLM entity generation failed for {Service}: {Error}", svc.Name, response.Error ?? "empty");
                return result;
            }

            // Parse delimited entity classes
            var content = response.Content.Trim();
            // Strip markdown fences if present
            if (content.StartsWith("```"))
            {
                var first = content.IndexOf('\n');
                if (first > 0) content = content[(first + 1)..];
                if (content.EndsWith("```")) content = content[..^3];
                content = content.Trim();
            }

            var sections = Regex.Split(content, @"^// === (\w+)\.cs ===\s*$",
                RegexOptions.Multiline);

            // sections: [preamble, name1, code1, name2, code2, ...]
            for (int i = 1; i + 1 < sections.Length; i += 2)
            {
                var entityName = sections[i].Trim();
                var entityCode = sections[i + 1].Trim();
                if (!string.IsNullOrWhiteSpace(entityCode) && svc.Entities.Contains(entityName, StringComparer.OrdinalIgnoreCase))
                    result[entityName] = entityCode;
            }

            _logger.LogInformation("LLM generated {Count}/{Total} entities for {Service}",
                result.Count, svc.Entities.Length, svc.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM entity generation threw for {Service}", svc.Name);
        }

        return result;
    }

    private static string GenerateDefaultEntity(MicroserviceDefinition svc, string entity) => $$"""
        using System.ComponentModel.DataAnnotations;
        namespace {{svc.Namespace}}.Data.Entities;

        public class {{entity}}
        {
            [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
            [Required] public string TenantId { get; set; } = null!;
            [Required] public string CreatedBy { get; set; } = null!;
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
            [Required] public string UpdatedBy { get; set; } = null!;
            public int VersionNo { get; set; } = 1;
        }
        """;

    // ─── LLM-Driven Migration Script ────────────────────────────────────────

    /// <summary>
    /// Generates migration SQL for a service via LLM. Falls back to default skeleton.
    /// </summary>
    private async Task<CodeArtifact> GenerateMigrationScriptAsync(
        MicroserviceDefinition svc, AgentContext context, CancellationToken ct)
    {
        var migrationSql = await GenerateServiceMigrationViaLlmAsync(svc, context, ct);
        var sql = migrationSql ?? GenerateDefaultMigrationSql(svc);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Migration,
            RelativePath = $"{svc.ProjectName}/Data/Migrations/V1__{svc.ShortName}_initial.sql",
            FileName = $"V1__{svc.ShortName}_initial.sql",
            Namespace = $"{svc.Namespace}.Data.Migrations",
            ProducedBy = AgentType.Database,
            Content = sql
        };
    }

    private async Task<string?> GenerateServiceMigrationViaLlmAsync(
        MicroserviceDefinition svc, AgentContext context, CancellationToken ct)
    {
        var requirementsSummary = string.Join("\n", context.Requirements
            .Take(50)
            .Select(r => $"- {r.Title}: {r.Description}"));

        // Domain-specific context for migration generation
        var domainMigrationCtx = "";
        var profile = context.DomainProfile;
        if (profile is not null)
        {
            var parts = new List<string>();
            if (profile.DomainGlossary is { Count: > 0 })
                parts.Add("Use these domain terms for column naming:\n" +
                    string.Join("\n", profile.DomainGlossary.Take(15).Select(kv => $"  {kv.Key} → {kv.Value}")));
            if (profile.SensitiveFieldPatterns is { Count: > 0 })
                parts.Add("Add COMMENT ON COLUMN for sensitive fields:\n" +
                    string.Join(", ", profile.SensitiveFieldPatterns.Take(15)));
            if (parts.Count > 0)
                domainMigrationCtx = "\n\nDomain Context:\n" + string.Join("\n", parts);
        }

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a senior PostgreSQL DBA generating migration DDL scripts.
                Generate CREATE TABLE statements for all entities in a microservice.

                Every table MUST have these standard columns:
                - id VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', '')
                - tenant_id VARCHAR(64) NOT NULL
                - created_at TIMESTAMPTZ NOT NULL DEFAULT now()
                - created_by VARCHAR(128) NOT NULL

                Add domain-specific columns based on entity name and context.
                Use snake_case for all column and table names.
                Include appropriate indexes (at minimum tenant_id).
                Include foreign key constraints where appropriate within the same schema.

                Output raw SQL only — no markdown fences, no commentary.
                Start with: CREATE SCHEMA IF NOT EXISTS {schema};
                """,
            UserPrompt = $"""
                Service: {svc.Name}
                Schema: {svc.Schema}
                Description: {svc.Description}
                Entities: {string.Join(", ", svc.Entities)}

                Project requirements context:
                {requirementsSummary}{domainMigrationCtx}

                Generate the complete migration SQL for all entities.
                """,
            Temperature = 0.2,
            MaxTokens = 4096,
            RequestingAgent = "DatabaseAgent"
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("LLM migration generation failed for {Service}: {Error}", svc.Name, response.Error ?? "empty");
                return null;
            }

            var sql = response.Content.Trim();
            // Strip markdown fences
            if (sql.StartsWith("```"))
            {
                var first = sql.IndexOf('\n');
                if (first > 0) sql = sql[(first + 1)..];
                if (sql.EndsWith("```")) sql = sql[..^3];
                sql = sql.Trim();
            }

            // Prepend header comment
            return $"""
                -- Migration: {svc.Name} initial schema (LLM-generated)
                -- Schema: {svc.Schema}
                -- Bounded Context: {svc.Description}

                {sql}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM migration generation threw for {Service}", svc.Name);
            return null;
        }
    }

    private static string GenerateDefaultMigrationSql(MicroserviceDefinition svc)
    {
        var tables = string.Join("\n\n", svc.Entities.Select(e =>
        {
            var table = ToSnakeCase(e);
            return $"""
                CREATE TABLE IF NOT EXISTS {svc.Schema}.{table} (
                    id          VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                    tenant_id   VARCHAR(64) NOT NULL,
                    created_by  VARCHAR(128) NOT NULL,
                    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                    updated_by  VARCHAR(128) NOT NULL,
                    version_no  INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS idx_{svc.Schema}_{table}_tenant ON {svc.Schema}.{table} (tenant_id);
                """;
        }));

        return $"""
            -- Migration: {svc.Name} initial schema
            -- Schema: {svc.Schema}
            -- Bounded Context: {svc.Description}

            CREATE SCHEMA IF NOT EXISTS {svc.Schema};

            {tables}
            """;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string ToSnakeCase(string s)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c) && i > 0) result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
