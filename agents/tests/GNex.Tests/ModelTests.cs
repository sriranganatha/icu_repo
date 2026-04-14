using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

public class ModelTests
{
    [Fact]
    public void AgentContext_InitializesWithUniqueRunId()
    {
        var ctx1 = new AgentContext();
        var ctx2 = new AgentContext();
        ctx1.RunId.Should().NotBe(ctx2.RunId);
    }

    [Fact]
    public void AgentContext_InitializesEmptyCollections()
    {
        var ctx = new AgentContext();
        ctx.Requirements.Should().BeEmpty();
        ctx.Artifacts.Should().BeEmpty();
        ctx.Findings.Should().BeEmpty();
        ctx.Messages.Should().BeEmpty();
        ctx.TestDiagnostics.Should().BeEmpty();
        ctx.RetryAttempts.Should().BeEmpty();
        ctx.AgentStatuses.Should().BeEmpty();
    }

    [Fact]
    public void AgentResult_DefaultsToNotSuccessful()
    {
        var result = new AgentResult { Agent = AgentType.Database };
        result.Success.Should().BeFalse();
        result.Artifacts.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.TestDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void CodeArtifact_InitializesWithUniqueId()
    {
        var a1 = new CodeArtifact();
        var a2 = new CodeArtifact();
        a1.Id.Should().NotBe(a2.Id);
    }

    [Fact]
    public void ReviewFinding_InitializesWithUniqueId()
    {
        var f1 = new ReviewFinding();
        var f2 = new ReviewFinding();
        f1.Id.Should().NotBe(f2.Id);
    }

    [Fact]
    public void TestDiagnostic_DefaultAttemptIsOne()
    {
        var d = new TestDiagnostic();
        d.AttemptNumber.Should().Be(1);
        d.Outcome.Should().Be(TestOutcome.Passed);
    }

    [Fact]
    public void PipelineConfig_HasSensibleDefaults()
    {
        var cfg = new PipelineConfig();
        cfg.DbPort.Should().Be(5418);
        cfg.DbName.Should().Be("gnex_db");
        cfg.SpinUpDocker.Should().BeTrue();
        cfg.ExecuteDdl.Should().BeTrue();
        cfg.SolutionNamespace.Should().Be("GNex");
    }

    [Theory]
    [InlineData(AgentType.Orchestrator)]
    [InlineData(AgentType.RequirementsReader)]
    [InlineData(AgentType.Database)]
    [InlineData(AgentType.ServiceLayer)]
    [InlineData(AgentType.Application)]
    [InlineData(AgentType.Integration)]
    [InlineData(AgentType.Review)]
    [InlineData(AgentType.Testing)]
    [InlineData(AgentType.Supervisor)]
    public void AgentType_AllValuesAreDefined(AgentType type)
    {
        Enum.IsDefined(type).Should().BeTrue();
    }

    [Theory]
    [InlineData(AgentStatus.Idle)]
    [InlineData(AgentStatus.Running)]
    [InlineData(AgentStatus.Completed)]
    [InlineData(AgentStatus.Failed)]
    public void AgentStatus_AllValuesAreDefined(AgentStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

    [Fact]
    public void AgentMessage_InitializesCorrectly()
    {
        var msg = new AgentMessage
        {
            From = AgentType.Supervisor,
            To = AgentType.Orchestrator,
            Subject = "Health check",
            Body = "All agents healthy"
        };
        msg.Id.Should().NotBeNullOrEmpty();
        msg.From.Should().Be(AgentType.Supervisor);
        msg.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void MicroserviceCatalog_IsEmptyLegacyFallback()
    {
        // MicroserviceCatalog.All is now empty — services are dynamically derived
        // from requirements by ArchitectAgent via ServiceCatalogResolver.
        var services = GNex.Core.Models.MicroserviceCatalog.All;
        services.Should().BeEmpty("services are now dynamically derived, not hardcoded");
    }

    [Fact]
    public void ServiceCatalogResolver_PrefersContextDerivedServices()
    {
        var ctx = new AgentContext();
        ctx.DerivedServices.Add(new MicroserviceDefinition
        {
            Name = "TestService", ShortName = "Test", Schema = "test",
            Description = "Test service", ApiPort = 5100,
            Entities = ["Entity1"], DependsOn = []
        });

        var services = ServiceCatalogResolver.GetServices(ctx);
        services.Should().HaveCount(1);
        services[0].Name.Should().Be("TestService");
    }

    [Fact]
    public void ServiceCatalogResolver_FallsBackToEmptyCatalog()
    {
        var ctx = new AgentContext();
        var services = ServiceCatalogResolver.GetServices(ctx);
        services.Should().BeEmpty("empty DerivedServices and empty MicroserviceCatalog");
    }
}
