using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Migration;

/// <summary>
/// Migration agent — generates EF Core migration files, SQL seed data scripts,
/// schema rollback scripts, and migration runner configurations for all HMS
/// microservices that have DbContext definitions.
/// </summary>
public sealed class MigrationAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<MigrationAgent> _logger;

    public AgentType Type => AgentType.Migration;
    public string Name => "Migration Agent";
    public string Description => "Generates EF Core migrations, seed data scripts, and schema rollback scripts for all microservices.";

    private static readonly string[] ServiceNames =
    [
        "PatientService", "EncounterService", "InpatientService", "EmergencyService",
        "DiagnosticsService", "RevenueService", "AuditService", "AiService"
    ];

    public MigrationAgent(ILlmProvider llm, ILogger<MigrationAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("MigrationAgent starting");

        var artifacts = new List<CodeArtifact>();

        try
        {
            foreach (var svc in ServiceNames)
            {
                ct.ThrowIfCancellationRequested();

                // Scan for DbContext and entity files
                var projDir = Path.Combine(context.OutputBasePath, "src", $"GNex.{svc}");
                var entities = new List<string>();
                var dbContextName = $"{svc}DbContext";

                if (Directory.Exists(projDir))
                {
                    var csFiles = Directory.GetFiles(projDir, "*.cs", SearchOption.AllDirectories);
                    foreach (var f in csFiles)
                    {
                        var content = await File.ReadAllTextAsync(f, ct);
                        if (content.Contains(": DbContext") || content.Contains(":DbContext"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(content, @"class\s+(\w+)\s*:\s*DbContext");
                            if (match.Success) dbContextName = match.Groups[1].Value;
                        }
                        if (content.Contains("public class") && (content.Contains("{ get; set; }") || content.Contains("{get;set;}")))
                        {
                            var entityMatch = System.Text.RegularExpressions.Regex.Match(content, @"public\s+(?:sealed\s+)?class\s+(\w+)");
                            if (entityMatch.Success) entities.Add(entityMatch.Groups[1].Value);
                        }
                    }
                }

                // Generate initial migration
                var migrationPrompt = $"""
                    Generate an EF Core initial migration for {svc} with DbContext "{dbContextName}".
                    Entities found: {string.Join(", ", entities.Take(10))}
                    
                    Generate a C# migration class with:
                    1. Up() method creating all tables with proper columns, indexes, and foreign keys
                    2. Down() method dropping all tables in reverse order
                    3. Proper use of migrationBuilder
                    
                    Use .NET 8 / EF Core 8 syntax. Namespace: GNex.{svc}.Migrations
                    Class name: InitialCreate
                    """;

                var migrationCode = await _llm.GenerateAsync(migrationPrompt, ct);
                artifacts.Add(new CodeArtifact
                {
                    Layer = ArtifactLayer.Database,
                    RelativePath = $"src/GNex.{svc}/Migrations/InitialCreate.cs",
                    FileName = "InitialCreate.cs",
                    Namespace = $"GNex.{svc}.Migrations",
                    ProducedBy = Type,
                    TracedRequirementIds = ["NFR-MIGRATION-01"],
                    Content = migrationCode
                });

                // Generate seed data script
                var seedPrompt = $"""
                    Generate a C# EF Core seed data class for {svc}.
                    DbContext: {dbContextName}. Entities: {string.Join(", ", entities.Take(10))}
                    
                    Create a static class {svc}DataSeeder with a method:
                      public static void Seed({dbContextName} context)
                    
                    Include realistic healthcare data (5-10 records per entity).
                    Use .NET 8 syntax. Namespace: GNex.{svc}.Data
                    """;

                var seedCode = await _llm.GenerateAsync(seedPrompt, ct);
                artifacts.Add(new CodeArtifact
                {
                    Layer = ArtifactLayer.Database,
                    RelativePath = $"src/GNex.{svc}/Data/{svc}DataSeeder.cs",
                    FileName = $"{svc}DataSeeder.cs",
                    Namespace = $"GNex.{svc}.Data",
                    ProducedBy = Type,
                    TracedRequirementIds = ["NFR-MIGRATION-02"],
                    Content = seedCode
                });

                // Generate rollback SQL script
                var rollbackPrompt = $"""
                    Generate a PostgreSQL rollback SQL script for {svc} that drops all tables,
                    indexes, and sequences created by the initial migration.
                    Schema: {MicroserviceCatalog.All.FirstOrDefault(s => s.Name == svc)?.Schema ?? "public"}
                    Entities: {string.Join(", ", entities.Take(10))}
                    Include IF EXISTS guards and CASCADE. Add header comment with service name and date placeholder.
                    """;

                var rollbackSql = await _llm.GenerateAsync(rollbackPrompt, ct);
                artifacts.Add(new CodeArtifact
                {
                    Layer = ArtifactLayer.Database,
                    RelativePath = $"src/GNex.{svc}/Migrations/rollback.sql",
                    FileName = "rollback.sql",
                    Namespace = string.Empty,
                    ProducedBy = Type,
                    TracedRequirementIds = ["NFR-MIGRATION-03"],
                    Content = rollbackSql
                });
            }

            // Generate migration runner configuration
            artifacts.Add(GenerateMigrationRunnerConfig());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Migration Agent: {artifacts.Count} artifacts — migrations, seed data, rollbacks for {ServiceNames.Length} services",
                Artifacts = artifacts,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "MigrationAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateMigrationRunnerConfig() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "scripts/run-migrations.ps1",
        FileName = "run-migrations.ps1",
        Namespace = string.Empty,
        ProducedBy = AgentType.Migration,
        TracedRequirementIds = ["NFR-MIGRATION-04"],
        Content = """
            # HMS Migration Runner
            # Runs EF Core migrations for all microservices in order
            param(
                [string]$ConnectionString = "Host=localhost;Port=5432;Database=hms;Username=hms;Password=gnex_dev_pw",
                [switch]$Rollback
            )

            $services = @(
                "GNex.PatientService",
                "GNex.EncounterService",
                "GNex.InpatientService",
                "GNex.EmergencyService",
                "GNex.DiagnosticsService",
                "GNex.RevenueService",
                "GNex.AuditService",
                "GNex.AiService"
            )

            foreach ($svc in $services) {
                Write-Host "Migrating $svc..." -ForegroundColor Cyan
                $proj = "src/$svc/$svc.csproj"
                if (Test-Path $proj) {
                    if ($Rollback) {
                        dotnet ef database update 0 --project $proj -- --connection "$ConnectionString"
                    } else {
                        dotnet ef database update --project $proj -- --connection "$ConnectionString"
                    }
                    if ($LASTEXITCODE -ne 0) {
                        Write-Host "FAILED: $svc" -ForegroundColor Red
                    } else {
                        Write-Host "OK: $svc" -ForegroundColor Green
                    }
                } else {
                    Write-Host "SKIPPED: $svc (project not found)" -ForegroundColor Yellow
                }
            }
            """
    };
}
