using FluentAssertions;
using Hms.Database.Entities.Platform;
using Hms.Database.Entities.Platform.AgentRegistry;
using Hms.Database.Entities.Platform.Technology;
using Hms.Database.Entities.Platform.Standards;
using Hms.Database.Entities.Platform.Workflows;
using Hms.Database.Entities.Platform.LlmConfig;

namespace HmsAgents.Tests;

/// <summary>
/// Tests for Phase 1 master metadata entities — validation, defaults, ID generation.
/// </summary>
public class PlatformEntityTests
{
    // ── PlatformEntityBase defaults ──

    [Fact]
    public void PlatformEntityBase_Defaults_SetsIdAndDates()
    {
        var entity = new Language();

        entity.Id.Should().NotBeNullOrEmpty();
        entity.Id.Should().HaveLength(32); // Guid without hyphens
        entity.IsActive.Should().BeTrue();
        entity.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        entity.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        entity.ArchivedAt.Should().BeNull();
        entity.VersionNo.Should().Be(1);
    }

    [Fact]
    public void PlatformEntityBase_UniqueIds()
    {
        var a = new Language();
        var b = new Framework();
        a.Id.Should().NotBe(b.Id);
    }

    // ── Technology entities ──

    [Fact]
    public void Language_DefaultProperties()
    {
        var lang = new Language { Name = "C#", Version = "12" };
        lang.Name.Should().Be("C#");
        lang.Version.Should().Be("12");
        lang.Status.Should().Be("active");
    }

    [Fact]
    public void Framework_RequiresLanguageId()
    {
        var fw = new Framework
        {
            Name = "ASP.NET Core",
            LanguageId = "abc123",
            Version = "9.0",
            Category = "backend"
        };
        fw.LanguageId.Should().NotBeNullOrEmpty();
        fw.Category.Should().Be("backend");
    }

    [Fact]
    public void DatabaseTechnology_DefaultPort()
    {
        var db = new DatabaseTechnology
        {
            Name = "PostgreSQL",
            DbType = "relational",
            DefaultPort = 5432
        };
        db.DefaultPort.Should().Be(5432);
        db.DbType.Should().Be("relational");
    }

    [Fact]
    public void CloudProvider_JsonArrayProperties()
    {
        var cp = new CloudProvider
        {
            Name = "AWS",
            RegionsJson = "[\"us-east-1\",\"eu-west-1\"]",
            ServicesJson = "[\"EC2\",\"S3\",\"RDS\"]"
        };
        cp.RegionsJson.Should().Contain("us-east-1");
        cp.ServicesJson.Should().Contain("S3");
    }

    // ── Agent registry entities ──

    [Fact]
    public void AgentTypeDefinition_CapabilitiesJson()
    {
        var def = new AgentTypeDefinition
        {
            Name = "Database",
            Description = "Generates database schemas",
            AgentTypeCode = "Database",
            CapabilitiesJson = "[\"code_generation\",\"schema_design\"]"
        };
        def.CapabilitiesJson.Should().Contain("code_generation");
        def.AgentTypeCode.Should().Be("Database");
    }

    [Fact]
    public void AgentModelMapping_CostTracking()
    {
        var mapping = new AgentModelMapping
        {
            AgentTypeDefinitionId = "def1",
            LlmProvider = "gemini",
            ModelId = "gemini-2.5-pro",
            TokenLimit = 32768,
            CostPer1kTokens = 0.05m
        };
        mapping.TokenLimit.Should().Be(32768);
        mapping.CostPer1kTokens.Should().Be(0.05m);
    }

    [Fact]
    public void AgentConstraint_Defaults()
    {
        var c = new AgentConstraint { AgentTypeDefinitionId = "def1" };
        c.MaxTokens.Should().Be(8192);
        c.MaxRetries.Should().Be(3);
        c.TimeoutSeconds.Should().Be(300);
    }

    // ── Template entities ──

    [Fact]
    public void CodeTemplate_BasicProperties()
    {
        var tpl = new CodeTemplate
        {
            Name = "Service Class",
            Content = "public class {{Name}}Service { }",
            TemplateType = "scaffold"
        };
        tpl.Content.Should().Contain("{{Name}}");
    }

    [Fact]
    public void BrdTemplate_BasicProperties()
    {
        var tpl = new BrdTemplate
        {
            Name = "Standard BRD",
            ProjectType = "web_app",
            SectionsJson = "[\"Introduction\",\"Scope\"]"
        };
        tpl.Name.Should().Be("Standard BRD");
    }

    // ── Standards entities ──

    [Fact]
    public void CodingStandard_LanguageAssociation()
    {
        var std = new CodingStandard
        {
            Name = "C# Coding Standards",
            LanguageId = "lang1",
            RulesJson = "{\"max_line_length\":120}"
        };
        std.LanguageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void QualityGate_DefaultThresholds()
    {
        var gate = new QualityGate
        {
            Name = "Default Gate",
            GateType = "coverage",
            ThresholdConfigJson = "{\"min_coverage\":80}"
        };
        gate.ThresholdConfigJson.Should().Contain("80");
    }

    // ── Workflow entities ──

    [Fact]
    public void SdlcWorkflow_DefaultNotDefault()
    {
        var wf = new SdlcWorkflow { Name = "Agile SDLC" };
        wf.IsDefault.Should().BeFalse();
        wf.Stages.Should().BeEmpty();
    }

    [Fact]
    public void StageDefinition_AgentsInvolvedJson()
    {
        var stage = new StageDefinition
        {
            WorkflowId = "wf1",
            Name = "Code Generation",
            Order = 3,
            AgentsInvolvedJson = "[\"Database\",\"ServiceLayer\",\"Application\"]"
        };
        stage.Order.Should().Be(3);
        stage.AgentsInvolvedJson.Should().Contain("ServiceLayer");
    }

    [Fact]
    public void ApprovalGateConfig_Defaults()
    {
        var gate = new ApprovalGateConfig
        {
            StageId = "s1",
            GateType = "human"
        };
        gate.TimeoutHours.Should().Be(24);
        gate.GateType.Should().Be("human");
    }

    [Fact]
    public void TransitionRule_AutoTransitionDefault()
    {
        var rule = new TransitionRule
        {
            FromStageId = "s1",
            ToStageId = "s2"
        };
        rule.AutoTransition.Should().BeTrue();
    }

    // ── LLM Config entities ──

    [Fact]
    public void LlmProviderConfig_BasicProperties()
    {
        var provider = new LlmProviderConfig
        {
            Name = "Google Gemini",
            ApiBaseUrl = "https://generativelanguage.googleapis.com",
            AuthType = "api_key"
        };
        provider.Name.Should().Be("Google Gemini");
        provider.AuthType.Should().Be("api_key");
    }

    [Fact]
    public void LlmModelConfig_CostTracking()
    {
        var model = new LlmModelConfig
        {
            ProviderId = "prov1",
            ModelName = "gemini-2.5-pro",
            ContextWindow = 1048576,
            CostInputPer1kTokens = 0.01m,
            CostOutputPer1kTokens = 0.03m
        };
        model.ContextWindow.Should().Be(1048576);
        model.CostInputPer1kTokens.Should().Be(0.01m);
    }
}
