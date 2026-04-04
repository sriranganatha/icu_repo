using FluentAssertions;
using HmsAgents.Agents.Supervisor;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HmsAgents.Tests;

public class SupervisorAgentTests
{
    private readonly Mock<ILogger<SupervisorAgent>> _loggerMock = new();

    private IServiceProvider BuildServiceProvider(params IAgent[] agents)
    {
        var sc = new ServiceCollection();
        foreach (var a in agents)
            sc.AddSingleton(a);
        sc.AddSingleton<IEnumerable<IAgent>>(agents);
        return sc.BuildServiceProvider();
    }

    private AgentContext CreateContextWithAllAgentsCompleted()
    {
        var ctx = new AgentContext
        {
            RequirementsBasePath = "/tmp/reqs",
            OutputBasePath = "/tmp/out"
        };

        // Mark all agents as completed
        foreach (var t in Enum.GetValues<AgentType>())
            ctx.AgentStatuses[t] = t == AgentType.Supervisor ? AgentStatus.Idle : AgentStatus.Completed;

        // Requirements
        ctx.Requirements.Add(new Requirement { Id = "REQ-001", Title = "Test req", Tags = ["Patient"] });

        // Database artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database, ProducedBy = AgentType.Database,
            FileName = "PatientProfile.cs", RelativePath = "Patient/Entities/PatientProfile.cs",
            Content = "public class PatientProfile { public string TenantId { get; set; } }",
            TracedRequirementIds = ["REQ-001"]
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database, ProducedBy = AgentType.Database,
            FileName = "PatientDbContext.cs", RelativePath = "Patient/Data/PatientDbContext.cs",
            Content = "public class PatientDbContext : DbContext { HasQueryFilter(e => e.TenantId == _tenantId) }",
            TracedRequirementIds = ["REQ-001"]
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Migration, ProducedBy = AgentType.Database,
            FileName = "V1__Patient_Tables.sql", RelativePath = "Patient/Migrations/V1.sql",
            Content = "CREATE TABLE patient_profile (tenant_id TEXT);"
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration, ProducedBy = AgentType.Database,
            FileName = "docker-compose.yml", RelativePath = "docker-compose.yml",
            Content = "version: '3.8'\nservices:\n  postgres:"
        });

        // Repository artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Repository, ProducedBy = AgentType.Database,
            FileName = "PatientRepository.cs", RelativePath = "Patient/Repositories/PatientRepository.cs",
            Content = "public class PatientRepository { private readonly string _tenantId; }"
        });

        // Service artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Dto, ProducedBy = AgentType.ServiceLayer,
            FileName = "PatientProfileDto.cs", RelativePath = "Patient/DTOs/PatientProfileDto.cs",
            Content = "public record PatientProfileDto(Guid Id, string Name);"
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Service, ProducedBy = AgentType.ServiceLayer,
            FileName = "IPatientService.cs", RelativePath = "Patient/Services/IPatientService.cs",
            Content = "public interface IPatientService { } // IntegrationEvent, Kafka"
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Service, ProducedBy = AgentType.ServiceLayer,
            FileName = "PatientService.cs", RelativePath = "Patient/Services/PatientService.cs",
            Content = "public class PatientService : IPatientService { Kafka producer }"
        });

        // Application artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration, ProducedBy = AgentType.Application,
            FileName = "Program.cs", RelativePath = "ApiGateway/Program.cs",
            Content = "var builder = WebApplication.CreateBuilder(); // Yarp ReverseProxy, MapGet, MapPost"
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration, ProducedBy = AgentType.Application,
            FileName = "TenantMiddleware.cs", RelativePath = "Shared/TenantMiddleware.cs",
            Content = "public class TenantMiddleware { } public class CorrelationIdMiddleware { }"
        });

        // Integration artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Integration, ProducedBy = AgentType.Integration,
            FileName = "KafkaConsumerHostedService.cs", RelativePath = "Integration/KafkaConsumerHostedService.cs",
            Content = "public class KafkaConsumerHostedService : BackgroundService { Outbox }"
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Integration, ProducedBy = AgentType.Integration,
            FileName = "FhirAdapter.cs", RelativePath = "Integration/FhirAdapter.cs",
            Content = "public class FhirAdapter { FHIR R4 Patient }"
        });

        // Test artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Test, ProducedBy = AgentType.Testing,
            FileName = "TenantIsolationTests.cs", RelativePath = "Tests/TenantIsolationTests.cs",
            Content = "public class TenantIsolationTests { [Fact] TenantIsolation }"
        });

        return ctx;
    }

    [Fact]
    public async Task Execute_AllAgentsHealthy_AllTestsPass()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.TestDiagnostics.Should().NotBeEmpty();
        result.TestDiagnostics.Where(d => d.Outcome == TestOutcome.Passed).Should().HaveCountGreaterThan(10);
        result.TestDiagnostics.Where(d => d.Outcome == TestOutcome.Failed).Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_MissingDbArtifacts_ReportsFailure()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.Artifacts.RemoveAll(a => a.ProducedBy == AgentType.Database);
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Where(d => d.Outcome == TestOutcome.Failed)
            .Should().Contain(d => d.TestName == "Database_ProducedArtifacts");
    }

    [Fact]
    public async Task Execute_FailedAgent_ReportsAgentHealthFailure()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.AgentStatuses[AgentType.Integration] = AgentStatus.Failed;
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.TestName == "Integration_Completed" && d.Outcome == TestOutcome.Failed);
    }

    [Fact]
    public async Task Execute_StaleRunningAgent_DetectsStall()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Running; // simulate stall
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.TestName == "Pipeline_NoStaleRunningAgents" && d.Outcome == TestOutcome.Failed);
    }

    [Fact]
    public async Task Execute_DuplicateArtifactPaths_DetectsIntegrityViolation()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database, ProducedBy = AgentType.Database,
            FileName = "PatientProfile.cs", RelativePath = "Patient/Entities/PatientProfile.cs",
            Content = "duplicate"
        });
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.TestName == "Pipeline_NoDuplicateArtifacts" && d.Outcome == TestOutcome.Failed);
    }

    [Fact]
    public async Task Execute_EmptyArtifact_DetectsIntegrityViolation()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database, ProducedBy = AgentType.Database,
            FileName = "Empty.cs", RelativePath = "Patient/Empty.cs",
            Content = "" // empty
        });
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.TestName == "Pipeline_NoEmptyArtifacts" && d.Outcome == TestOutcome.Failed);
    }

    [Fact]
    public async Task Execute_FailedAgent_AttemptsRemediation()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Failed;
        ctx.Artifacts.RemoveAll(a => a.ProducedBy == AgentType.Database);

        // Provide a mock agent that succeeds on retry
        var dbAgentMock = new Mock<IAgent>();
        dbAgentMock.Setup(a => a.Type).Returns(AgentType.Database);
        dbAgentMock.Setup(a => a.Name).Returns("Database");
        dbAgentMock.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext c, CancellationToken _) =>
            {
                c.AgentStatuses[AgentType.Database] = AgentStatus.Completed;
                return new AgentResult { Agent = AgentType.Database, Success = true, Summary = "Fixed" };
            });

        var agent = new SupervisorAgent(BuildServiceProvider(dbAgentMock.Object), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.Category == "Remediation" && d.Outcome == TestOutcome.Remediated);
    }

    [Fact]
    public async Task Execute_ReportsCorrectSummary()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.Summary.Should().Contain("Supervisor:");
        result.Summary.Should().Contain("passed");
        result.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Execute_NoRequirements_FailsRequirementCheck()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        ctx.Requirements.Clear();
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.TestName == "Requirements_NonEmpty" && d.Outcome == TestOutcome.Failed);
    }

    [Fact]
    public async Task Execute_MissingKafkaIntegration_DetectsGap()
    {
        // Arrange
        var ctx = CreateContextWithAllAgentsCompleted();
        // Remove all Kafka-related content from service artifacts
        ctx.Artifacts.RemoveAll(a => a.ProducedBy == AgentType.ServiceLayer);
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Service, ProducedBy = AgentType.ServiceLayer,
            FileName = "IPatientService.cs", RelativePath = "Patient/Services/IPatientService.cs",
            Content = "public interface IPatientService { }" // No Kafka mention
        });
        var agent = new SupervisorAgent(BuildServiceProvider(), _loggerMock.Object);

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert
        result.TestDiagnostics.Should().Contain(d =>
            d.TestName == "Service_HasKafkaIntegration" && d.Outcome == TestOutcome.Failed);
    }
}
