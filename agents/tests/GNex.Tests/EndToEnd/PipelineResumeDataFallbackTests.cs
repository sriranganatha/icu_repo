using FluentAssertions;
using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Tests for pipeline resume fallback logic: when prior runId has no data,
/// the system should fall back to the latest available data from any run.
/// Also covers failure record storage and retrieval.
/// </summary>
public class PipelineResumeDataFallbackTests
{
    private readonly Mock<IArtifactWriter> _writerMock = new();
    private readonly Mock<IPipelineEventSink> _sinkMock = new();
    private readonly Mock<IAuditLogger> _auditMock = new();
    private readonly Mock<ILlmProvider> _llmMock = new();
    private readonly Mock<IHumanGate> _humanGateMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _loggerMock = new();

    public PipelineResumeDataFallbackTests()
    {
        _llmMock.SetupGet(x => x.IsAvailable).Returns(true);
        _llmMock.SetupGet(x => x.ProviderName).Returns("test-llm");
        _llmMock
            .Setup(x => x.GenerateAsync(It.IsAny<LlmPrompt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Success = true,
                Content = "objective\nsteps\nrisks",
                Model = "test-model"
            });
    }

    private PipelineConfig CreateConfig() => new()
    {
        RequirementsPath = Path.Combine(Path.GetTempPath(), $"gnex-resume-fb-{Guid.NewGuid():N}"),
        OutputPath = Path.Combine(Path.GetTempPath(), $"gnex-out-fb-{Guid.NewGuid():N}"),
        SpinUpDocker = false,
        ExecuteDdl = false
    };

    private AgentOrchestrator CreateOrchestrator(List<IAgent> agents)
        => new(agents, _llmMock.Object, _writerMock.Object, _sinkMock.Object,
            _auditMock.Object, _humanGateMock.Object, _serviceProviderMock.Object, _loggerMock.Object);

    private static Mock<IAgent> CreateAgentMock(AgentType type, string name, bool success = true)
    {
        var mock = new Mock<IAgent>();
        mock.Setup(a => a.Type).Returns(type);
        mock.Setup(a => a.Name).Returns(name);
        mock.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                ctx.AgentStatuses[type] = success ? AgentStatus.Completed : AgentStatus.Failed;
                return new AgentResult
                {
                    Agent = type, Success = success, Summary = $"{name} done"
                };
            });
        return mock;
    }

    // ────────────────────────────────────────────────
    //  Resume with pre-loaded requirements
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Resume_WithRequirements_PreservesInContext()
    {
        var dbMock = CreateAgentMock(AgentType.Database, "Database");
        var agents = new List<IAgent> { dbMock.Object };

        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string>
            { "RequirementsReader", "RequirementsExpander" };
        config.ResumeRequirements =
        [
            new Requirement { Id = "REQ-001", Title = "User management", Description = "Manage users" },
            new Requirement { Id = "REQ-002", Title = "Role permissions", Description = "RBAC" }
        ];

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "stub.md"), "# Stub");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.Requirements.Should().HaveCountGreaterThanOrEqualTo(2);
        ctx.Requirements.Should().Contain(r => r.Id == "REQ-001");
        ctx.Requirements.Should().Contain(r => r.Id == "REQ-002");
    }

    // ────────────────────────────────────────────────
    //  Resume with derived services
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Resume_WithDerivedServices_PopulatesContext()
    {
        var dbMock = CreateAgentMock(AgentType.Database, "Database");
        var agents = new List<IAgent> { dbMock.Object };

        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string>
            { "RequirementsReader", "Architect" };
        config.ResumeRequirements =
        [
            new Requirement { Id = "REQ-001", Title = "Test", Description = "Test" }
        ];
        config.ResumeDerivedServices =
        [
            new MicroserviceDefinition
            {
                Name = "PatientService", ShortName = "patient", Schema = "patient_schema",
                Description = "Patient bounded context", ApiPort = 5200,
                Entities = ["Patient", "Address"], DependsOn = []
            }
        ];

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "stub.md"), "# Stub");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.DerivedServices.Should().HaveCount(1);
        ctx.DerivedServices[0].Name.Should().Be("PatientService");
    }

    // ────────────────────────────────────────────────
    //  Resume with null requirements — should not throw
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Resume_NullRequirements_DoesNotThrow()
    {
        var agents = new List<IAgent> { CreateAgentMock(AgentType.Database, "Database").Object };
        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string> { "RequirementsReader" };
        config.ResumeRequirements = null;

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "stub.md"), "# Stub");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var act = () => orchestrator.RunPipelineAsync(config, cts.Token);
        await act.Should().NotThrowAsync();
    }

    // ────────────────────────────────────────────────
    //  Resume: empty CompletedAgents set — normal run
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Resume_EmptyCompletedAgents_RunsAllAgents()
    {
        var reqMock = CreateAgentMock(AgentType.RequirementsReader, "ReqReader");
        var agents = new List<IAgent> { reqMock.Object };

        var config = CreateConfig();
        config.ResumeCompletedAgents = []; // empty set — no agents to skip

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "stub.md"), "# Stub");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        // Should run RequirementsReader since it wasn't in the "completed" set
        reqMock.Verify(
            a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
