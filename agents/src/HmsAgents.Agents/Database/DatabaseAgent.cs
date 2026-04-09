using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Database;

/// <summary>
/// Generates database artifacts per microservice: each bounded context gets its own
/// DbContext, entities, repositories, and migration scripts — aligned to schema-per-service.
/// Also provisions Docker PostgreSQL and executes DDL when configured.
/// </summary>
public sealed class DatabaseAgent : IAgent
{
    private readonly ILogger<DatabaseAgent> _logger;
    // Track which artifact paths this agent has already produced in this pipeline run
    private readonly HashSet<string> _generatedPaths = new(StringComparer.OrdinalIgnoreCase);
    // Track whether DDL has been executed this pipeline run
    private bool _ddlExecutedThisRun;

    public AgentType Type => AgentType.Database;
    public string Name => "Database Agent";
    public string Description => "Generates per-microservice EF Core entities, DbContext, repositories, migrations, and provisions Docker PostgreSQL.";

    public DatabaseAgent(ILogger<DatabaseAgent> logger) => _logger = logger;

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

            // Generate per-service database artifacts (only for NEW services)
            foreach (var svc in newServices)
            {
                _logger.LogInformation("Generating DB layer for {Service} ({Schema})", svc.Name, svc.Schema);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Generating DB layer for {svc.Name} — schema: {svc.Schema}, entities: {string.Join(", ", svc.Entities)}");

                foreach (var entity in svc.Entities)
                    AddIfNew(artifacts, GenerateEntity(svc, entity));

                AddIfNew(artifacts, GenerateDbContext(svc));

                foreach (var entity in svc.Entities)
                    AddIfNew(artifacts, GenerateRepository(svc, entity));

                AddIfNew(artifacts, GenerateMigrationScript(svc));

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
                        Namespace = "Hms.Infrastructure",
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

    // ─── Per-Service Migration Script ───────────────────────────────────────

    private static CodeArtifact GenerateMigrationScript(MicroserviceDefinition svc)
    {
        var tables = string.Join("\n\n",
            svc.Entities.Select(e => GenerateCreateTableSql(svc.Schema, e)));

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Migration,
            RelativePath = $"{svc.ProjectName}/Data/Migrations/V1__{svc.ShortName}_initial.sql",
            FileName = $"V1__{svc.ShortName}_initial.sql",
            Namespace = $"{svc.Namespace}.Data.Migrations",
            ProducedBy = AgentType.Database,
            Content = $"""
                -- Migration: {svc.Name} initial schema
                -- Schema: {svc.Schema}
                -- Bounded Context: {svc.Description}

                CREATE SCHEMA IF NOT EXISTS {svc.Schema};

                {tables}
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
            Namespace = "Hms.Infrastructure",
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
                var dbUser = config?.DbUser ?? "hms";
                var dbPassword = config?.DbPassword ?? "hms_dev_pw";
                var dbName = config?.DbName ?? "hms";
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
                        Namespace = "Hms.Infrastructure",
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
                                            dockerfile: Hms.ApiGateway/Dockerfile
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
        var archInstruction = context.OrchestratorInstructions
            .FirstOrDefault(i => i.StartsWith("[ARCH]", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(archInstruction))
            return MicroserviceCatalog.All.ToList();

        var marker = "TARGET_SERVICES=";
        var start = archInstruction.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return MicroserviceCatalog.All.ToList();

        start += marker.Length;
        var end = archInstruction.IndexOf(';', start);
        var csv = end >= 0 ? archInstruction[start..end] : archInstruction[start..];

        var resolved = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MicroserviceCatalog.ByName)
            .Where(s => s is not null)
            .Cast<MicroserviceDefinition>()
            .ToList();

        return resolved.Count > 0 ? resolved : MicroserviceCatalog.All.ToList();
    }

    /// <summary>
    /// Resolve microservices from assigned backlog items by matching tags, module, and title keywords
    /// to services in the catalog. Falls back to full catalog if no matches found.
    /// </summary>
    private static List<MicroserviceDefinition> ResolveServicesFromBacklogItems(
        List<ExpandedRequirement> items, AgentContext context)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var text = $"{item.Title} {item.Description} {item.Module} {string.Join(" ", item.Tags)}";

            foreach (var svc in MicroserviceCatalog.All)
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

        return MicroserviceCatalog.All.Where(s => matched.Contains(s.Name)).ToList();
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

    // ─── Entity Generation ──────────────────────────────────────────────────

    private static CodeArtifact GenerateEntity(MicroserviceDefinition svc, string entity)
    {
        var body = EntityBodies.TryGetValue(entity, out var gen) ? gen(svc) : GenerateDefaultEntity(svc, entity);
        return new CodeArtifact
        {
            Layer = ArtifactLayer.Database,
            RelativePath = $"{svc.ProjectName}/Data/Entities/{entity}.cs",
            FileName = $"{entity}.cs",
            Namespace = $"{svc.Namespace}.Data.Entities",
            ProducedBy = AgentType.Database,
            Content = body
        };
    }

    // Registry mapping entity name → body generator
    private static readonly Dictionary<string, Func<MicroserviceDefinition, string>> EntityBodies = new()
    {
        ["PatientProfile"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class PatientProfile
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                public string? FacilityId { get; set; }
                [Required] public string EnterprisePersonKey { get; set; } = null!;
                [Required] public string LegalGivenName { get; set; } = null!;
                [Required] public string LegalFamilyName { get; set; } = null!;
                public string? PreferredName { get; set; }
                public DateOnly DateOfBirth { get; set; }
                public string? SexAtBirth { get; set; }
                public string? PrimaryLanguage { get; set; }
                [Required] public string StatusCode { get; set; } = "active";
                [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
                public bool LegalHoldFlag { get; set; }
                public string? SourceSystem { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
                public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string UpdatedBy { get; set; } = null!;
                public int VersionNo { get; set; } = 1;
                public ICollection<PatientIdentifier> Identifiers { get; set; } = [];
            }
            """,

        ["PatientIdentifier"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class PatientIdentifier
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string IdentifierType { get; set; } = null!;
                [Required] public string IdentifierValueHash { get; set; } = null!;
                public string? Issuer { get; set; }
                [Required] public string StatusCode { get; set; } = "active";
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                public PatientProfile Patient { get; set; } = null!;
            }
            """,

        ["Encounter"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class Encounter
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                [Required] public string FacilityId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string EncounterType { get; set; } = null!;
                public string? SourcePathway { get; set; }
                public string? AttendingProviderRef { get; set; }
                public DateTimeOffset StartAt { get; set; }
                public DateTimeOffset? EndAt { get; set; }
                [Required] public string StatusCode { get; set; } = "active";
                [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
                public bool LegalHoldFlag { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
                public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string UpdatedBy { get; set; } = null!;
                public int VersionNo { get; set; } = 1;
                public ICollection<ClinicalNote> Notes { get; set; } = [];
            }
            """,

        ["ClinicalNote"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class ClinicalNote
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string EncounterId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string NoteType { get; set; } = null!;
                public string? NoteClassificationCode { get; set; }
                [Required] public string ContentJson { get; set; } = "{}";
                public string? AiInteractionId { get; set; }
                public DateTimeOffset AuthoredAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string AuthoredBy { get; set; } = null!;
                public string? AmendedFromNoteId { get; set; }
                public int VersionNo { get; set; } = 1;
                public bool LegalHoldFlag { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                public Encounter Encounter { get; set; } = null!;
            }
            """,

        ["Admission"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class Admission
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                [Required] public string FacilityId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string EncounterId { get; set; } = null!;
                [Required] public string AdmitClass { get; set; } = null!;
                public string? AdmitSource { get; set; }
                [Required] public string StatusCode { get; set; } = "active";
                public DateTimeOffset? ExpectedDischargeAt { get; set; }
                public string? UtilizationStatusCode { get; set; }
                [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
                public bool LegalHoldFlag { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
                public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string UpdatedBy { get; set; } = null!;
                public int VersionNo { get; set; } = 1;
            }
            """,

        ["AdmissionEligibility"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class AdmissionEligibility
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string FacilityId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string EncounterId { get; set; } = null!;
                public string? CandidateClass { get; set; }
                [Required] public string DecisionCode { get; set; } = null!;
                public string? RationaleJson { get; set; }
                public string? PayerAuthorizationStatus { get; set; }
                public bool OverrideFlag { get; set; }
                public string? ApprovedBy { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
            }
            """,

        ["EmergencyArrival"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class EmergencyArrival
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                [Required] public string FacilityId { get; set; } = null!;
                public string? PatientId { get; set; }
                public string? TemporaryIdentityAlias { get; set; }
                public string? ArrivalMode { get; set; }
                public string? ChiefComplaint { get; set; }
                public string? HandoffSource { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
                public ICollection<TriageAssessment> Triages { get; set; } = [];
            }
            """,

        ["TriageAssessment"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class TriageAssessment
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string ArrivalId { get; set; } = null!;
                public string? PatientId { get; set; }
                [Required] public string AcuityLevel { get; set; } = null!;
                public string? ChiefComplaint { get; set; }
                public string VitalSnapshotJson { get; set; } = "{}";
                public bool ReTriageFlag { get; set; }
                public string? PathwayRecommendation { get; set; }
                public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string PerformedBy { get; set; } = null!;
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                public EmergencyArrival Arrival { get; set; } = null!;
            }
            """,

        ["ResultRecord"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class ResultRecord
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                [Required] public string FacilityId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string OrderId { get; set; } = null!;
                [Required] public string AnalyteCode { get; set; } = null!;
                public string? MeasuredValue { get; set; }
                public string? UnitCode { get; set; }
                public string? AbnormalFlag { get; set; }
                public bool CriticalFlag { get; set; }
                public DateTimeOffset ResultAt { get; set; }
                [Required] public string RecordedBy { get; set; } = null!;
                [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
                public bool LegalHoldFlag { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
                public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string UpdatedBy { get; set; } = null!;
                public int VersionNo { get; set; } = 1;
            }
            """,

        ["Claim"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class Claim
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                [Required] public string FacilityId { get; set; } = null!;
                [Required] public string PatientId { get; set; } = null!;
                [Required] public string EncounterRef { get; set; } = null!;
                [Required] public string PayerRef { get; set; } = null!;
                [Required] public string ClaimStatus { get; set; } = null!;
                public decimal BilledAmount { get; set; }
                public decimal? AllowedAmount { get; set; }
                [Required] public string ClassificationCode { get; set; } = "financial_sensitive";
                public bool LegalHoldFlag { get; set; }
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
                public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string UpdatedBy { get; set; } = null!;
                public int VersionNo { get; set; } = 1;
            }
            """,

        ["AuditEvent"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class AuditEvent
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                public string? FacilityId { get; set; }
                [Required] public string EventType { get; set; } = null!;
                [Required] public string EntityType { get; set; } = null!;
                [Required] public string EntityId { get; set; } = null!;
                [Required] public string ActorType { get; set; } = null!;
                [Required] public string ActorId { get; set; } = null!;
                [Required] public string CorrelationId { get; set; } = null!;
                [Required] public string ClassificationCode { get; set; } = null!;
                public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string PayloadJson { get; set; } = "{}";
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
            }
            """,

        ["AiInteraction"] = svc => $$"""
            using System.ComponentModel.DataAnnotations;
            namespace {{svc.Namespace}}.Data.Entities;

            public class AiInteraction
            {
                [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
                [Required] public string TenantId { get; set; } = null!;
                [Required] public string RegionId { get; set; } = null!;
                public string? FacilityId { get; set; }
                [Required] public string InteractionType { get; set; } = null!;
                public string? EncounterId { get; set; }
                public string? PatientId { get; set; }
                [Required] public string ModelVersion { get; set; } = null!;
                [Required] public string PromptVersion { get; set; } = null!;
                public string? InputSummaryJson { get; set; }
                public string? OutputSummaryJson { get; set; }
                [Required] public string OutcomeCode { get; set; } = null!;
                public string? AcceptedBy { get; set; }
                public string? RejectedBy { get; set; }
                public string? OverrideReason { get; set; }
                [Required] public string ClassificationCode { get; set; } = "ai_evidence";
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                [Required] public string CreatedBy { get; set; } = null!;
            }
            """
    };

    private static string GenerateDefaultEntity(MicroserviceDefinition svc, string entity) => $$"""
        using System.ComponentModel.DataAnnotations;
        namespace {{svc.Namespace}}.Data.Entities;

        public class {{entity}}
        {
            [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
            [Required] public string TenantId { get; set; } = null!;
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        }
        """;

    // ─── SQL Generation ─────────────────────────────────────────────────────

    private static string GenerateCreateTableSql(string schema, string entity)
    {
        var table = ToSnakeCase(entity);
        return TableSql.TryGetValue(entity, out var sql) ? sql(schema) : $$"""
            CREATE TABLE IF NOT EXISTS {{schema}}.{{table}} (
                id          VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id   VARCHAR(64) NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;
    }

    private static readonly Dictionary<string, Func<string, string>> TableSql = new()
    {
        ["PatientProfile"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.patient_profile (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64),
                enterprise_person_key   VARCHAR(128) NOT NULL,
                legal_given_name        VARCHAR(256) NOT NULL,
                legal_family_name       VARCHAR(256) NOT NULL,
                preferred_name          VARCHAR(256),
                date_of_birth           DATE NOT NULL,
                sex_at_birth            VARCHAR(16),
                primary_language        VARCHAR(16),
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                source_system           VARCHAR(64),
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_{s}_patient_tenant_epk ON {s}.patient_profile (tenant_id, enterprise_person_key);
            """,

        ["PatientIdentifier"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.patient_identifier (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL REFERENCES {s}.patient_profile(id),
                identifier_type         VARCHAR(64) NOT NULL,
                identifier_value_hash   VARCHAR(256) NOT NULL,
                issuer                  VARCHAR(128),
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_{s}_pid_type_hash ON {s}.patient_identifier (tenant_id, identifier_type, identifier_value_hash);
            """,

        ["Encounter"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.encounter (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_type          VARCHAR(64) NOT NULL,
                source_pathway          VARCHAR(64),
                attending_provider_ref  VARCHAR(128),
                start_at                TIMESTAMPTZ NOT NULL,
                end_at                  TIMESTAMPTZ,
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_{s}_enc_tenant_patient ON {s}.encounter (tenant_id, patient_id);
            """,

        ["ClinicalNote"] = s => $$"""
            CREATE TABLE IF NOT EXISTS {{s}}.clinical_note (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                encounter_id            VARCHAR(32) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                note_type               VARCHAR(64) NOT NULL,
                note_classification_code VARCHAR(64),
                content_json            JSONB NOT NULL DEFAULT '{}'::jsonb,
                ai_interaction_id       VARCHAR(32),
                authored_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
                authored_by             VARCHAR(128) NOT NULL,
                amended_from_note_id    VARCHAR(32),
                version_no              INTEGER NOT NULL DEFAULT 1,
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_{{s}}_note_encounter ON {{s}}.clinical_note (tenant_id, encounter_id);
            """,

        ["Admission"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.admission (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_id            VARCHAR(32) NOT NULL,
                admit_class             VARCHAR(64) NOT NULL,
                admit_source            VARCHAR(64),
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                expected_discharge_at   TIMESTAMPTZ,
                utilization_status_code VARCHAR(32),
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_{s}_adm_tenant_patient ON {s}.admission (tenant_id, patient_id);
            """,

        ["AdmissionEligibility"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.admission_eligibility (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_id            VARCHAR(32) NOT NULL,
                candidate_class         VARCHAR(64),
                decision_code           VARCHAR(32) NOT NULL,
                rationale_json          JSONB,
                payer_authorization_status VARCHAR(32),
                override_flag           BOOLEAN NOT NULL DEFAULT FALSE,
                approved_by             VARCHAR(128),
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL
            );
            """,

        ["EmergencyArrival"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.emergency_arrival (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32),
                temporary_identity_alias VARCHAR(128),
                arrival_mode            VARCHAR(64),
                chief_complaint         TEXT,
                handoff_source          VARCHAR(128),
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_{s}_ea_tenant_facility ON {s}.emergency_arrival (tenant_id, facility_id);
            """,

        ["TriageAssessment"] = s => $$"""
            CREATE TABLE IF NOT EXISTS {{s}}.triage_assessment (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                arrival_id              VARCHAR(32) NOT NULL REFERENCES {{s}}.emergency_arrival(id),
                patient_id              VARCHAR(32),
                acuity_level            VARCHAR(16) NOT NULL,
                chief_complaint         TEXT,
                vital_snapshot_json     JSONB NOT NULL DEFAULT '{}'::jsonb,
                re_triage_flag          BOOLEAN NOT NULL DEFAULT FALSE,
                pathway_recommendation  VARCHAR(64),
                performed_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
                performed_by            VARCHAR(128) NOT NULL,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """,

        ["ResultRecord"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.result_record (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                order_id                VARCHAR(32) NOT NULL,
                analyte_code            VARCHAR(64) NOT NULL,
                measured_value          VARCHAR(256),
                unit_code               VARCHAR(32),
                abnormal_flag           VARCHAR(16),
                critical_flag           BOOLEAN NOT NULL DEFAULT FALSE,
                result_at               TIMESTAMPTZ NOT NULL,
                recorded_by             VARCHAR(128) NOT NULL,
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_{s}_rr_tenant_patient ON {s}.result_record (tenant_id, patient_id);
            """,

        ["Claim"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.claim (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_ref           VARCHAR(32) NOT NULL,
                payer_ref               VARCHAR(128) NOT NULL,
                claim_status            VARCHAR(32) NOT NULL,
                billed_amount           NUMERIC(14,2) NOT NULL DEFAULT 0,
                allowed_amount          NUMERIC(14,2),
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'financial_sensitive',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_{s}_claim_tenant_patient ON {s}.claim (tenant_id, patient_id);
            """,

        ["AuditEvent"] = s => $$"""
            CREATE TABLE IF NOT EXISTS {{s}}.audit_event (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64),
                event_type              VARCHAR(64) NOT NULL,
                entity_type             VARCHAR(64) NOT NULL,
                entity_id               VARCHAR(128) NOT NULL,
                actor_type              VARCHAR(32) NOT NULL,
                actor_id                VARCHAR(128) NOT NULL,
                correlation_id          VARCHAR(128) NOT NULL,
                classification_code     VARCHAR(64) NOT NULL,
                occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
                payload_json            JSONB NOT NULL DEFAULT '{}'::jsonb,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_{{s}}_audit_tenant_entity ON {{s}}.audit_event (tenant_id, entity_type, entity_id);
            CREATE INDEX IF NOT EXISTS idx_{{s}}_audit_correlation ON {{s}}.audit_event (correlation_id);
            """,

        ["AiInteraction"] = s => $"""
            CREATE TABLE IF NOT EXISTS {s}.ai_interaction (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64),
                interaction_type        VARCHAR(64) NOT NULL,
                encounter_id            VARCHAR(32),
                patient_id              VARCHAR(32),
                model_version           VARCHAR(64) NOT NULL,
                prompt_version          VARCHAR(64) NOT NULL,
                input_summary_json      JSONB,
                output_summary_json     JSONB,
                outcome_code            VARCHAR(32) NOT NULL,
                accepted_by             VARCHAR(128),
                rejected_by             VARCHAR(128),
                override_reason         TEXT,
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'ai_evidence',
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_{s}_ai_tenant_encounter ON {s}.ai_interaction (tenant_id, encounter_id);
            """
    };

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
