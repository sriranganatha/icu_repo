using GNex.Database;
using GNex.Database.Entities.Platform;
using GNex.Database.Entities.Platform.Technology;
using GNex.Database.Entities.Platform.Standards;
using GNex.Database.Entities.Platform.LlmConfig;
using GNex.Database.Entities.Platform.AgentRegistry;
using GNex.Database.Entities.Platform.Configuration;
using GNex.Database.Entities.Platform.Workflows;
using GNex.Database.Repositories;
using FluentAssertions;

namespace GNex.Tests.Integration;

/// <summary>
/// Integration tests for the generic PlatformRepository covering:
///   CRUD lifecycle, soft-delete &amp; restore, pagination, query filtering,
///   version increment, and timestamp management for the GNex development platform.
/// Scenarios use realistic multi-domain project data (fintech, SaaS, e-commerce).
/// </summary>
public sealed class PlatformRepositoryIntegrationTests : IDisposable
{
    private readonly TestDbFixture _fix = new();

    // ────────────────────── Language CRUD ──────────────────────

    [Fact]
    public async Task Language_CreateAndRetrieve_FullLifecycle()
    {
        var repo = _fix.CreateRepo<Language>();

        var lang = new Language
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "C#",
            Version = "12.0",
            Status = "active",
            FileExtensionsJson = """[".cs",".csx"]"""
        };

        var created = await repo.CreateAsync(lang);
        var fetched = await repo.GetByIdAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("C#");
        fetched.Version.Should().Be("12.0");
        fetched.TenantId.Should().Be(TestDbFixture.TestTenantId);
        fetched.FileExtensionsJson.Should().Contain(".cs");
    }

    [Fact]
    public async Task Language_SoftDelete_ExcludesFromList()
    {
        var repo = _fix.CreateRepo<Language>();

        var python = await repo.CreateAsync(new Language
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Python",
            Version = "3.12",
            Status = "active",
            FileExtensionsJson = """[".py"]"""
        });

        await repo.CreateAsync(new Language
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Rust",
            Version = "1.77",
            Status = "active",
            FileExtensionsJson = """[".rs"]"""
        });

        await repo.SoftDeleteAsync(python.Id);

        var active = await repo.ListAsync(0, 50);
        active.Should().Contain(l => l.Name == "Rust");
        active.Should().NotContain(l => l.Name == "Python");
    }

    [Fact]
    public async Task Language_Restore_ReActivates()
    {
        var repo = _fix.CreateRepo<Language>();

        var lang = await repo.CreateAsync(new Language
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Go",
            Version = "1.22",
            Status = "active",
            FileExtensionsJson = """[".go"]"""
        });

        await repo.SoftDeleteAsync(lang.Id);
        await repo.RestoreAsync(lang.Id);

        var restored = await repo.GetByIdAsync(lang.Id);
        restored!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Language_Update_IncrementsVersion()
    {
        var repo = _fix.CreateRepo<Language>();

        var lang = await repo.CreateAsync(new Language
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "TypeScript",
            Version = "5.3",
            Status = "active",
            FileExtensionsJson = """[".ts",".tsx"]"""
        });

        lang.Version = "5.4";
        await repo.UpdateAsync(lang);

        var updated = await repo.GetByIdAsync(lang.Id);
        updated!.Version.Should().Be("5.4");
        updated.VersionNo.Should().BeGreaterThan(1);
    }

    // ────────────────────── Framework CRUD ──────────────────────

    [Fact]
    public async Task Framework_CreateMultiple_QueryByCategory()
    {
        var repo = _fix.CreateRepo<Framework>();
        var langId = Guid.NewGuid().ToString("N");

        await repo.CreateAsync(new Framework
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "ASP.NET Core",
            LanguageId = langId,
            Version = "9.0",
            Category = "web"
        });

        await repo.CreateAsync(new Framework
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Entity Framework Core",
            LanguageId = langId,
            Version = "9.0",
            Category = "data"
        });

        await repo.CreateAsync(new Framework
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Django",
            LanguageId = Guid.NewGuid().ToString("N"),
            Version = "5.0",
            Category = "web"
        });

        var webFrameworks = await repo.QueryAsync(f => f.Category == "web");
        webFrameworks.Should().HaveCount(2);
        webFrameworks.Should().AllSatisfy(f => f.Category.Should().Be("web"));
    }

    // ────────────────────── DatabaseTechnology ──────────────────────

    [Fact]
    public async Task DatabaseTechnology_CRUD_MultiEngine()
    {
        var repo = _fix.CreateRepo<DatabaseTechnology>();

        await repo.CreateAsync(new DatabaseTechnology
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "PostgreSQL",
            DbType = "relational",
            DefaultPort = 5432,
            ConnectionTemplate = "Host={host};Port={port};Database={db};Username={user};Password={pass}"
        });

        await repo.CreateAsync(new DatabaseTechnology
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Redis",
            DbType = "key_value",
            DefaultPort = 6379
        });

        await repo.CreateAsync(new DatabaseTechnology
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Neo4j",
            DbType = "graph",
            DefaultPort = 7687
        });

        var list = await repo.ListAsync(0, 50);
        list.Should().HaveCount(3);

        var relational = await repo.CountAsync(d => d.DbType == "relational");
        relational.Should().Be(1);
    }

    // ────────────────────── Standards ──────────────────────

    [Fact]
    public async Task CodingStandard_CreateWithRules()
    {
        var repo = _fix.CreateRepo<CodingStandard>();

        var standard = await repo.CreateAsync(new CodingStandard
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "C# Clean Architecture Standard",
            RulesJson = """["no_public_fields","use_dependency_injection","async_suffix_on_async_methods"]""",
            LinterConfig = "dotnet_diagnostic.CA1051.severity = error"
        });

        var fetched = await repo.GetByIdAsync(standard.Id);
        fetched.Should().NotBeNull();
        fetched!.RulesJson.Should().Contain("no_public_fields");
    }

    [Fact]
    public async Task SecurityPolicy_CreateOwasp()
    {
        var repo = _fix.CreateRepo<SecurityPolicy>();

        var policy = await repo.CreateAsync(new SecurityPolicy
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "OWASP Top 10 Compliance",
            Category = "compliance",
            RulesJson = """["no_sql_injection","no_xss","no_csrf","encrypt_sensitive_data"]""",
            Severity = "critical"
        });

        policy.Id.Should().NotBeNullOrEmpty();
        policy.Category.Should().Be("compliance");
        policy.Severity.Should().Be("critical");
    }

    [Fact]
    public async Task QualityGate_CoverageThreshold()
    {
        var repo = _fix.CreateRepo<QualityGate>();

        var gate = await repo.CreateAsync(new QualityGate
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Minimum Code Coverage",
            GateType = "coverage",
            ThresholdConfigJson = """{"min_coverage":80,"exclude_generated_code":true}"""
        });

        var fetched = await repo.GetByIdAsync(gate.Id);
        fetched!.GateType.Should().Be("coverage");
        fetched.ThresholdConfigJson.Should().Contain("min_coverage");
    }

    [Fact]
    public async Task NamingConvention_MultiScope()
    {
        var repo = _fix.CreateRepo<NamingConvention>();

        await repo.CreateAsync(new NamingConvention
        {
            TenantId = TestDbFixture.TestTenantId,
            Scope = "class",
            Pattern = "PascalCase",
            ExamplesJson = """["ProjectService","BrdWorkflowManager"]"""
        });

        await repo.CreateAsync(new NamingConvention
        {
            TenantId = TestDbFixture.TestTenantId,
            Scope = "db_table",
            Pattern = "snake_case",
            ExamplesJson = """["project","brd_document","agent_run"]"""
        });

        var list = await repo.ListAsync(0, 50);
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReviewChecklist_SecurityScope()
    {
        var repo = _fix.CreateRepo<ReviewChecklist>();

        var checklist = await repo.CreateAsync(new ReviewChecklist
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Security Code Review Checklist",
            Scope = "security",
            ChecklistItemsJson = """["Check for SQL injection","Validate all user inputs","Ensure secrets not hardcoded"]"""
        });

        var fetched = await repo.GetByIdAsync(checklist.Id);
        fetched!.Scope.Should().Be("security");
        fetched.ChecklistItemsJson.Should().Contain("SQL injection");
    }

    // ────────────────────── LLM Config ──────────────────────

    [Fact]
    public async Task LlmProviderConfig_CreateGemini()
    {
        var repo = _fix.CreateRepo<LlmProviderConfig>();

        var provider = await repo.CreateAsync(new LlmProviderConfig
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "gemini",
            ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            AuthType = "api_key",
            RateLimitPerMinute = 60,
            IsAvailable = true
        });

        var fetched = await repo.GetByIdAsync(provider.Id);
        fetched!.Name.Should().Be("gemini");
        fetched.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task LlmModelConfig_CreateWithCost()
    {
        var repo = _fix.CreateRepo<LlmModelConfig>();

        var model = await repo.CreateAsync(new LlmModelConfig
        {
            TenantId = TestDbFixture.TestTenantId,
            ProviderId = Guid.NewGuid().ToString("N"),
            ModelName = "gemini-2.5-pro",
            ContextWindow = 1_048_576,
            CostInputPer1kTokens = 0.00125m,
            CostOutputPer1kTokens = 0.005m,
            CapabilitiesJson = """["code","reasoning","vision","long_context"]"""
        });

        var fetched = await repo.GetByIdAsync(model.Id);
        fetched!.ContextWindow.Should().Be(1_048_576);
        fetched.CostInputPer1kTokens.Should().Be(0.00125m);
    }

    [Fact]
    public async Task LlmRoutingRule_PrimaryFallback()
    {
        var repo = _fix.CreateRepo<LlmRoutingRule>();

        var rule = await repo.CreateAsync(new LlmRoutingRule
        {
            TenantId = TestDbFixture.TestTenantId,
            TaskType = "code_generation",
            PrimaryModelId = "model-gemini-pro",
            FallbackModelId = "model-gpt-4o",
            ConditionsJson = """{"max_tokens":8192,"language":"csharp"}""",
            Priority = 10
        });

        var fetched = await repo.GetByIdAsync(rule.Id);
        fetched!.TaskType.Should().Be("code_generation");
        fetched.FallbackModelId.Should().Be("model-gpt-4o");
    }

    [Fact]
    public async Task TokenBudget_ProjectScope()
    {
        var repo = _fix.CreateRepo<TokenBudget>();

        var budget = await repo.CreateAsync(new TokenBudget
        {
            TenantId = TestDbFixture.TestTenantId,
            Scope = "per_project",
            BudgetTokens = 5_000_000,
            AlertThreshold = 0.75,
            ProjectId = "proj-fintech-001"
        });

        var fetched = await repo.GetByIdAsync(budget.Id);
        fetched!.BudgetTokens.Should().Be(5_000_000);
        fetched.AlertThreshold.Should().Be(0.75);
    }

    // ────────────────────── Agent Registry ──────────────────────

    [Fact]
    public async Task AgentTypeDefinition_RegisterCodeGenerator()
    {
        var repo = _fix.CreateRepo<AgentTypeDefinition>();

        var agent = await repo.CreateAsync(new AgentTypeDefinition
        {
            TenantId = TestDbFixture.TestTenantId,
            AgentTypeCode = "CodeGenerator",
            Name = "Code Generator Agent",
            Description = "Generates production-quality source code from enriched requirements and architecture specs",
            CapabilitiesJson = """["code_generation","refactoring","unit_test_generation"]"""
        });

        var fetched = await repo.GetByIdAsync(agent.Id);
        fetched!.AgentTypeCode.Should().Be("CodeGenerator");
        fetched.CapabilitiesJson.Should().Contain("code_generation");
    }

    // ────────────────────── SDLC Workflow ──────────────────────

    [Fact]
    public async Task SdlcWorkflow_CreateWithStages()
    {
        var wfRepo = _fix.CreateRepo<SdlcWorkflow>();
        var stageRepo = _fix.CreateRepo<StageDefinition>();

        var wf = await wfRepo.CreateAsync(new SdlcWorkflow
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Standard Agile Pipeline",
            Description = "Full SDLC: Requirements → Architecture → Code → Test → Review → Deploy",
            IsDefault = true
        });

        var stages = new[]
        {
            new StageDefinition { TenantId = TestDbFixture.TestTenantId, WorkflowId = wf.Id, Name = "Requirements", Order = 1, AgentsInvolvedJson = """["RequirementsReader"]""" },
            new StageDefinition { TenantId = TestDbFixture.TestTenantId, WorkflowId = wf.Id, Name = "Architecture", Order = 2, AgentsInvolvedJson = """["Architecture"]""" },
            new StageDefinition { TenantId = TestDbFixture.TestTenantId, WorkflowId = wf.Id, Name = "Code", Order = 3, AgentsInvolvedJson = """["Database","ServiceLayer","Integration"]""" },
            new StageDefinition { TenantId = TestDbFixture.TestTenantId, WorkflowId = wf.Id, Name = "Test", Order = 4, AgentsInvolvedJson = """["Testing","LoadTest"]""" },
            new StageDefinition { TenantId = TestDbFixture.TestTenantId, WorkflowId = wf.Id, Name = "Review", Order = 5, AgentsInvolvedJson = """["Review","Security","CodeQuality"]""" },
        };
        foreach (var s in stages)
            await stageRepo.CreateAsync(s);

        var stageList = await stageRepo.QueryAsync(s => s.WorkflowId == wf.Id);
        stageList.Should().HaveCount(5);
    }

    [Fact]
    public async Task TransitionRule_AutoTransition()
    {
        var stageRepo = _fix.CreateRepo<StageDefinition>();
        var trRepo = _fix.CreateRepo<TransitionRule>();
        var wfId = Guid.NewGuid().ToString("N");

        var s1 = await stageRepo.CreateAsync(new StageDefinition
        {
            TenantId = TestDbFixture.TestTenantId, WorkflowId = wfId, Name = "Code", Order = 1,
            AgentsInvolvedJson = """["CodeGenerator"]""", ExitCriteria = "all_files_generated"
        });
        var s2 = await stageRepo.CreateAsync(new StageDefinition
        {
            TenantId = TestDbFixture.TestTenantId, WorkflowId = wfId, Name = "Test", Order = 2,
            AgentsInvolvedJson = """["Testing"]""", EntryCriteria = "code_stage_complete"
        });

        var rule = await trRepo.CreateAsync(new TransitionRule
        {
            TenantId = TestDbFixture.TestTenantId,
            FromStageId = s1.Id,
            ToStageId = s2.Id,
            ConditionsJson = """{"all_tests_pass":true,"coverage_min":80}""",
            AutoTransition = true
        });

        var fetched = await trRepo.GetByIdAsync(rule.Id);
        fetched!.AutoTransition.Should().BeTrue();
        fetched.FromStageId.Should().Be(s1.Id);
    }

    // ────────────────────── Configuration ──────────────────────

    [Fact]
    public async Task CompatibilityRule_FrameworkConflict()
    {
        var repo = _fix.CreateRepo<CompatibilityRule>();

        var rule = await repo.CreateAsync(new CompatibilityRule
        {
            TenantId = TestDbFixture.TestTenantId,
            SourceTechnologyId = "lang-csharp",
            SourceTechnologyCategory = "language",
            TargetTechnologyId = "fw-django",
            TargetTechnologyCategory = "framework",
            Compatibility = "incompatible",
            Reason = "Django requires Python, not compatible with C#"
        });

        var fetched = await repo.GetByIdAsync(rule.Id);
        fetched!.Compatibility.Should().Be("incompatible");
    }

    [Fact]
    public async Task StarterKit_FullStackTemplate()
    {
        var repo = _fix.CreateRepo<StarterKit>();

        var kit = await repo.CreateAsync(new StarterKit
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Full-Stack SaaS Starter",
            Description = "Production-ready SaaS template: ASP.NET Core + React + PostgreSQL",
            TechStackJson = """[{"category":"language","technologyId":"csharp","version":"12.0"}]""",
            ArchitecturePattern = "clean_architecture",
            SortOrder = 1
        });

        var fetched = await repo.GetByIdAsync(kit.Id);
        fetched!.ArchitecturePattern.Should().Be("clean_architecture");
    }

    // ────────────────────── Pagination Edge Cases ──────────────────────

    [Fact]
    public async Task Pagination_EmptySet_ReturnsEmptyList()
    {
        var repo = _fix.CreateRepo<CloudProvider>();
        var list = await repo.ListAsync(0, 50);
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Pagination_SkipBeyondTotal_ReturnsEmpty()
    {
        var repo = _fix.CreateRepo<DevOpsTool>();
        await repo.CreateAsync(new DevOpsTool
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "GitHub Actions",
            Category = "ci_cd"
        });
        var list = await repo.ListAsync(skip: 100, take: 10);
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task CountAsync_ExcludesSoftDeleted()
    {
        var repo = _fix.CreateRepo<QualityGate>();

        var gate1 = await repo.CreateAsync(new QualityGate
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Coverage >= 80%",
            GateType = "coverage",
            ThresholdConfigJson = """{"min_coverage":80}"""
        });
        await repo.CreateAsync(new QualityGate
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "No Critical Vulns",
            GateType = "security",
            ThresholdConfigJson = """{"max_critical":0}"""
        });

        await repo.SoftDeleteAsync(gate1.Id);
        var count = await repo.CountAsync();
        count.Should().Be(1);
    }

    public void Dispose() => _fix.Dispose();
}
