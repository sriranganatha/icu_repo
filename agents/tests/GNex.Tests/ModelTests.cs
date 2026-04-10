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
    public void MicroserviceCatalog_Has8Services()
    {
        var services = GNex.Core.Models.MicroserviceCatalog.All;
        services.Should().HaveCount(8);
        services.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        services.Select(s => s.ApiPort).Should().OnlyHaveUniqueItems();
        services.Select(s => s.Schema).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void MicroserviceCatalog_AllServicesHaveEntities()
    {
        foreach (var svc in MicroserviceCatalog.All)
        {
            svc.Entities.Should().NotBeEmpty($"{svc.Name} should have entities");
            svc.Namespace.Should().NotBeNullOrEmpty();
            svc.ProjectName.Should().NotBeNullOrEmpty();
            svc.DbContextName.Should().NotBeNullOrEmpty();
        }
    }
}
