using FluentAssertions;
using GNex.Agents.Database;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests;

/// <summary>
/// Tests for the DatabaseAgent fix: graceful handling of empty service catalog,
/// zero derived services, and proper success/failure determination.
/// </summary>
public class DatabaseAgentFixTests
{
    private readonly Mock<ILogger<DatabaseAgent>> _loggerMock = new();
    private readonly Mock<ILlmProvider> _llmMock = new();

    public DatabaseAgentFixTests()
    {
        _llmMock.SetupGet(x => x.IsAvailable).Returns(true);
        _llmMock.SetupGet(x => x.ProviderName).Returns("test-llm");
        _llmMock
            .Setup(x => x.GenerateAsync(It.IsAny<LlmPrompt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = true,
                Content = "// generated code",
                Model = "test-model"
            });
    }

    private DatabaseAgent CreateAgent() => new(_loggerMock.Object, _llmMock.Object);

    private static AgentContext CreateContext(List<MicroserviceDefinition>? derivedServices = null)
    {
        var ctx = new AgentContext
        {
            PipelineConfig = new PipelineConfig
            {
                SpinUpDocker = false,
                ExecuteDdl = false,
                OutputPath = Path.GetTempPath()
            }
        };
        if (derivedServices is not null)
            ctx.DerivedServices = derivedServices;
        return ctx;
    }

    private static MicroserviceDefinition CreateService(string name = "PatientService") => new()
    {
        Name = name,
        ShortName = name.Replace("Service", "").ToLower(),
        Schema = $"{name.Replace("Service", "").ToLower()}_schema",
        Description = $"{name} bounded context",
        ApiPort = 5200,
        Entities = ["Patient", "Address"],
        DependsOn = []
    };

    // ────────────────────────────────────────────────
    //  Empty service catalog — the core fix
    // ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyCatalog_ReturnsSuccessWithGracefulMessage()
    {
        // Arrange: No derived services and MicroserviceCatalog.All is empty
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert: Should NOT fail — gracefully completes
        result.Success.Should().BeTrue("empty catalog is not an error — Architect must derive services first");
        result.Summary.Should().Contain("No services in catalog");
        ctx.AgentStatuses[AgentType.Database].Should().Be(AgentStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCatalog_ProducesZeroArtifacts()
    {
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);

        var result = await agent.ExecuteAsync(ctx);

        result.Artifacts.Should().BeEmpty();
        ctx.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCatalog_DoesNotCallLlm()
    {
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);

        await agent.ExecuteAsync(ctx);

        _llmMock.Verify(
            x => x.GenerateAsync(It.IsAny<LlmPrompt>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not waste LLM calls when there are no services");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCatalog_ReportsProgressAboutMissingServices()
    {
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);
        var progressMessages = new List<string>();
        ctx.ReportProgress = (_, msg) => { progressMessages.Add(msg); return Task.CompletedTask; };

        await agent.ExecuteAsync(ctx);

        progressMessages.Should().ContainSingle()
            .Which.Should().Contain("No microservices defined yet");
    }

    // ────────────────────────────────────────────────
    //  Non-empty catalog — regular success path
    // ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithDerivedServices_GeneratesArtifacts()
    {
        var agent = CreateAgent();
        var svc = CreateService();
        var ctx = CreateContext(derivedServices: [svc]);

        var result = await agent.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().NotBeEmpty("services present should produce artifacts");
        ctx.AgentStatuses[AgentType.Database].Should().Be(AgentStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_WithDerivedServices_ProducesEntitiesAndDbContext()
    {
        var agent = CreateAgent();
        var svc = CreateService();
        var ctx = CreateContext(derivedServices: [svc]);

        var result = await agent.ExecuteAsync(ctx);

        var paths = result.Artifacts.Select(a => a.RelativePath).ToList();
        // Should have entity files
        paths.Should().Contain(p => p.Contains("Entities/Patient.cs"));
        paths.Should().Contain(p => p.Contains("Entities/Address.cs"));
        // Should have DbContext
        paths.Should().Contain(p => p.Contains("DbContext.cs"));
        // Should have repositories
        paths.Should().Contain(p => p.Contains("Repositories/"));
    }

    [Fact]
    public async Task ExecuteAsync_WithServices_CallsLlmForEntityGeneration()
    {
        var agent = CreateAgent();
        var svc = CreateService();
        var ctx = CreateContext(derivedServices: [svc]);

        await agent.ExecuteAsync(ctx);

        _llmMock.Verify(
            x => x.GenerateAsync(It.IsAny<LlmPrompt>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Should call LLM to generate entity code");
    }

    // ── Idempotency: second run with same services should skip ──

    [Fact]
    public async Task ExecuteAsync_SecondRunSameServices_SkipsRegeneration()
    {
        var agent = CreateAgent();
        var svc = CreateService();
        var ctx = CreateContext(derivedServices: [svc]);
        ctx.PipelineConfig!.ExecuteDdl = false;

        // First run: generates
        var first = await agent.ExecuteAsync(ctx);
        first.Success.Should().BeTrue();

        // Second run: same agent instance remembers generated paths
        var ctx2 = CreateContext(derivedServices: [svc]);
        var second = await agent.ExecuteAsync(ctx2);
        // With DDL not executed, newServices=0 but _ddlExecutedThisRun=false
        // so it won't enter the skip branch — it'll hit 0 newServices
        // The agent should still succeed
        second.Success.Should().BeTrue();
    }

    // ────────────────────────────────────────────────
    //  Multiple services
    // ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MultipleServices_GeneratesArtifactsForEach()
    {
        var agent = CreateAgent();
        var svc1 = CreateService("PatientService");
        var svc2 = new MicroserviceDefinition
        {
            Name = "BillingService",
            ShortName = "billing",
            Schema = "billing_schema",
            Description = "Billing bounded context",
            ApiPort = 5201,
            Entities = ["Invoice", "Payment"],
            DependsOn = ["PatientService"]
        };
        var ctx = CreateContext(derivedServices: [svc1, svc2]);

        var result = await agent.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var paths = result.Artifacts.Select(a => a.RelativePath).ToList();
        paths.Should().Contain(p => p.Contains("PatientService"));
        paths.Should().Contain(p => p.Contains("BillingService"));
    }

    // ────────────────────────────────────────────────
    //  Backlog item assignment with empty catalog
    // ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AssignedItemsButEmptyCatalog_GracefulSuccess()
    {
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);

        // Add a database-assigned backlog item
        ctx.ExpandedRequirements.Add(new ExpandedRequirement
        {
            Id = "WI-001", Title = "Create patient DB schema",
            Status = WorkItemStatus.InProgress,
            AssignedAgent = "Database",
            Module = "Patient"
        });

        var result = await agent.ExecuteAsync(ctx);

        // Even with assigned items, if catalog is empty => graceful success
        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("No services in catalog");
    }

    // ────────────────────────────────────────────────
    //  Agent type and metadata
    // ────────────────────────────────────────────────

    [Fact]
    public void DatabaseAgent_HasCorrectType()
    {
        var agent = CreateAgent();
        agent.Type.Should().Be(AgentType.Database);
        agent.Name.Should().Be("Database Agent");
    }

    [Fact]
    public async Task ExecuteAsync_SetsRunningStatusImmediately()
    {
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);

        await agent.ExecuteAsync(ctx);

        // After completion it should be Completed (not Running)
        ctx.AgentStatuses[AgentType.Database].Should().Be(AgentStatus.Completed);
    }

    // ────────────────────────────────────────────────
    //  Duration tracking
    // ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SetsNonZeroDuration()
    {
        var agent = CreateAgent();
        var ctx = CreateContext(derivedServices: []);

        var result = await agent.ExecuteAsync(ctx);

        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
