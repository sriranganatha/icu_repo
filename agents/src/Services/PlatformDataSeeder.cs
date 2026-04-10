using GNex.Database;
using GNex.Database.Entities.Platform.Configuration;
using GNex.Database.Entities.Platform.Technology;
using GNex.Database.Entities.Platform.LlmConfig;
using GNex.Database.Entities.Platform.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

/// <summary>Seeds default platform metadata on first run.</summary>
public class PlatformDataSeeder(GNexDbContext db, ILogger<PlatformDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Languages.AnyAsync(ct)) return; // Already seeded

        logger.LogInformation("Seeding platform metadata...");

        // ── Languages ──
        var csharp = new Language { Name = "C#", Version = "12", FileExtensionsJson = "[\".cs\"]" };
        var python = new Language { Name = "Python", Version = "3.12", FileExtensionsJson = "[\".py\"]" };
        var typescript = new Language { Name = "TypeScript", Version = "5.5", FileExtensionsJson = "[\".ts\",\".tsx\"]" };
        var java = new Language { Name = "Java", Version = "21", FileExtensionsJson = "[\".java\"]" };
        var go = new Language { Name = "Go", Version = "1.22", FileExtensionsJson = "[\".go\"]" };
        var dart = new Language { Name = "Dart", Version = "3.4", FileExtensionsJson = "[\".dart\"]" };
        var rust = new Language { Name = "Rust", Version = "1.78", FileExtensionsJson = "[\".rs\"]" };
        db.Languages.AddRange(csharp, python, typescript, java, go, dart, rust);

        // ── Frameworks ──
        db.Frameworks.AddRange(
            new Framework { Name = "ASP.NET Core", LanguageId = csharp.Id, Version = "9.0", Category = "backend" },
            new Framework { Name = "Blazor", LanguageId = csharp.Id, Version = "9.0", Category = "frontend" },
            new Framework { Name = "FastAPI", LanguageId = python.Id, Version = "0.111", Category = "backend" },
            new Framework { Name = "Django", LanguageId = python.Id, Version = "5.0", Category = "backend" },
            new Framework { Name = "Next.js", LanguageId = typescript.Id, Version = "14", Category = "fullstack" },
            new Framework { Name = "React", LanguageId = typescript.Id, Version = "18", Category = "frontend" },
            new Framework { Name = "Angular", LanguageId = typescript.Id, Version = "18", Category = "frontend" },
            new Framework { Name = "Express.js", LanguageId = typescript.Id, Version = "4", Category = "backend" },
            new Framework { Name = "Spring Boot", LanguageId = java.Id, Version = "3.3", Category = "backend" },
            new Framework { Name = "Flutter", LanguageId = dart.Id, Version = "3.22", Category = "mobile" },
            new Framework { Name = "Gin", LanguageId = go.Id, Version = "1.10", Category = "backend" },
            new Framework { Name = "Actix-web", LanguageId = rust.Id, Version = "4", Category = "backend" }
        );

        // ── Database Technologies ──
        db.DatabaseTechnologies.AddRange(
            new DatabaseTechnology { Name = "PostgreSQL", DbType = "relational", DefaultPort = 5432, ConnectionTemplate = "Host={host};Port={port};Database={db};Username={user};Password={pass}" },
            new DatabaseTechnology { Name = "SQL Server", DbType = "relational", DefaultPort = 1433 },
            new DatabaseTechnology { Name = "MySQL", DbType = "relational", DefaultPort = 3306 },
            new DatabaseTechnology { Name = "MongoDB", DbType = "document", DefaultPort = 27017 },
            new DatabaseTechnology { Name = "Redis", DbType = "cache", DefaultPort = 6379 },
            new DatabaseTechnology { Name = "Snowflake", DbType = "warehouse" },
            new DatabaseTechnology { Name = "SQLite", DbType = "embedded" }
        );

        // ── Cloud Providers ──
        db.CloudProviders.AddRange(
            new CloudProvider { Name = "AWS", RegionsJson = "[\"us-east-1\",\"us-west-2\",\"eu-west-1\"]", ServicesJson = "[\"EC2\",\"ECS\",\"Lambda\",\"S3\",\"RDS\"]" },
            new CloudProvider { Name = "Azure", RegionsJson = "[\"eastus\",\"westus2\",\"westeurope\"]", ServicesJson = "[\"App Service\",\"AKS\",\"Functions\",\"SQL Database\"]" },
            new CloudProvider { Name = "GCP", RegionsJson = "[\"us-central1\",\"us-east1\",\"europe-west1\"]", ServicesJson = "[\"Cloud Run\",\"GKE\",\"Cloud Functions\",\"Cloud SQL\"]" },
            new CloudProvider { Name = "Vercel", RegionsJson = "[\"global\"]", ServicesJson = "[\"Serverless Functions\",\"Edge Functions\"]" }
        );

        // ── DevOps Tools ──
        db.DevOpsTools.AddRange(
            new DevOpsTool { Name = "GitHub Actions", Category = "ci_cd" },
            new DevOpsTool { Name = "Terraform", Category = "iac" },
            new DevOpsTool { Name = "Docker", Category = "container" },
            new DevOpsTool { Name = "Kubernetes", Category = "orchestration" },
            new DevOpsTool { Name = "Helm", Category = "packaging" },
            new DevOpsTool { Name = "ArgoCD", Category = "gitops" },
            new DevOpsTool { Name = "Airflow", Category = "orchestration" }
        );

        // ── Starter Kits ──
        db.StarterKits.AddRange(
            new StarterKit
            {
                Name = "SaaS API",
                Description = "FastAPI + PostgreSQL + Redis + Docker + GitHub Actions. Production-ready API.",
                Icon = "bi-cloud-arrow-up",
                ArchitecturePattern = "microservices",
                TechStackJson = """[{"layer":"backend","type":"language","id":"python"},{"layer":"backend","type":"framework","id":"fastapi"},{"layer":"data","type":"database","id":"postgresql"},{"layer":"data","type":"cache","id":"redis"}]""",
                SortOrder = 1
            },
            new StarterKit
            {
                Name = "Full Stack Web",
                Description = "Next.js + Node.js + PostgreSQL + Vercel. SSR, API routes, automated deployments.",
                Icon = "bi-window-stack",
                ArchitecturePattern = "monolith",
                TechStackJson = """[{"layer":"fullstack","type":"language","id":"typescript"},{"layer":"fullstack","type":"framework","id":"nextjs"},{"layer":"data","type":"database","id":"postgresql"}]""",
                SortOrder = 2
            },
            new StarterKit
            {
                Name = "Data Pipeline",
                Description = "Python + Airflow + Snowflake + dbt. Scheduled data ingestion and transformation.",
                Icon = "bi-diagram-2",
                ArchitecturePattern = "pipeline",
                TechStackJson = """[{"layer":"backend","type":"language","id":"python"},{"layer":"orchestration","type":"tool","id":"airflow"},{"layer":"data","type":"warehouse","id":"snowflake"}]""",
                SortOrder = 3
            },
            new StarterKit
            {
                Name = ".NET Enterprise",
                Description = "ASP.NET Core + SQL Server + Azure + Terraform. Clean architecture, CQRS.",
                Icon = "bi-building",
                ArchitecturePattern = "clean_architecture",
                TechStackJson = """[{"layer":"backend","type":"language","id":"csharp"},{"layer":"backend","type":"framework","id":"aspnet-core"},{"layer":"data","type":"database","id":"sqlserver"}]""",
                SortOrder = 4
            },
            new StarterKit
            {
                Name = "Mobile App",
                Description = "Flutter + Firebase + GCP. Cross-platform iOS/Android with real-time sync.",
                Icon = "bi-phone",
                ArchitecturePattern = "bloc_pattern",
                TechStackJson = """[{"layer":"mobile","type":"language","id":"dart"},{"layer":"mobile","type":"framework","id":"flutter"}]""",
                SortOrder = 5
            }
        );

        // ── Default LLM Provider ──
        var geminiProvider = new LlmProviderConfig
        {
            Name = "Google Gemini",
            ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            AuthType = "api_key"
        };
        db.LlmProviderConfigs.Add(geminiProvider);
        db.LlmModelConfigs.AddRange(
            new LlmModelConfig { ProviderId = geminiProvider.Id, ModelName = "gemini-2.5-pro", ContextWindow = 65536, CostInputPer1kTokens = 0.00125m, CostOutputPer1kTokens = 0.005m },
            new LlmModelConfig { ProviderId = geminiProvider.Id, ModelName = "gemini-2.0-flash", ContextWindow = 32768, CostInputPer1kTokens = 0.0001m, CostOutputPer1kTokens = 0.0004m }
        );

        // ── Default SDLC Workflow ──
        var workflow = new SdlcWorkflow { Name = "Standard SDLC", Description = "Full software development lifecycle", IsDefault = true };
        db.SdlcWorkflows.Add(workflow);
        db.StageDefinitions.AddRange(
            new StageDefinition { WorkflowId = workflow.Id, Name = "Requirements", Order = 1 },
            new StageDefinition { WorkflowId = workflow.Id, Name = "Architecture", Order = 2 },
            new StageDefinition { WorkflowId = workflow.Id, Name = "Implementation", Order = 3 },
            new StageDefinition { WorkflowId = workflow.Id, Name = "Testing", Order = 4 },
            new StageDefinition { WorkflowId = workflow.Id, Name = "Review", Order = 5 },
            new StageDefinition { WorkflowId = workflow.Id, Name = "Deployment", Order = 6 }
        );

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Platform metadata seeded successfully");
    }
}
