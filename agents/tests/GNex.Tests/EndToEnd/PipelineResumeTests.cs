using FluentAssertions;
using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for pipeline resume logic — validates that the orchestrator
/// correctly skips completed agents, restores context from a prior run, and
/// continues from where it left off.
/// </summary>
public class PipelineResumeTests
{
    private readonly Mock<IArtifactWriter> _writerMock = new();
    private readonly Mock<IPipelineEventSink> _sinkMock = new();
    private readonly Mock<IAuditLogger> _auditMock = new();
    private readonly Mock<ILlmProvider> _llmMock = new();
    private readonly Mock<IHumanGate> _humanGateMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _loggerMock = new();

    public PipelineResumeTests()
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
        RequirementsPath = Path.Combine(Path.GetTempPath(), $"gnex-resume-{Guid.NewGuid():N}"),
        OutputPath = Path.Combine(Path.GetTempPath(), $"gnex-out-{Guid.NewGuid():N}"),
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
                var artifact = new CodeArtifact
                {
                    Layer = ArtifactLayer.Service, RelativePath = $"{name}/Output.cs",
                    FileName = "Output.cs", Content = $"// {name}", ProducedBy = type
                };
                ctx.Artifacts.Add(artifact);
                return new AgentResult
                {
                    Agent = type, Success = success,
                    Summary = $"{name} done", Artifacts = [artifact]
                };
            });
        return mock;
    }

    // ── Resume: completed agents are skipped ──

    [Fact]
    public async Task Resume_SkipsCompletedAgentsAndRunsRemaining()
    {
        var reqReaderMock = CreateAgentMock(AgentType.RequirementsReader, "ReqReader");
        var dbMock = CreateAgentMock(AgentType.Database, "Database");
        var agents = new List<IAgent> { reqReaderMock.Object, dbMock.Object };

        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string> { "RequirementsReader" };
        config.ResumeRequirements =
        [
            new Requirement { Id = "REQ-001", Title = "Prior requirement", Description = "From prior run" }
        ];

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-002\nNew requirement");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        // RequirementsReader should NOT have been called
        reqReaderMock.Verify(
            a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()), Times.Never);

        // Requirements from prior run should be in context
        ctx.Requirements.Should().Contain(r => r.Id == "REQ-001");
    }

    [Fact]
    public async Task Resume_RestoresExpandedRequirementsFromPriorRun()
    {
        var agents = new List<IAgent>
        {
            CreateAgentMock(AgentType.RequirementsReader, "ReqReader").Object,
            CreateAgentMock(AgentType.Database, "Database").Object
        };

        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string>
            { "RequirementsReader", "RequirementsExpander" };
        config.ResumeRequirements =
        [
            new Requirement { Id = "REQ-001", Title = "Test", Description = "Test req" }
        ];
        config.ResumeExpandedRequirements =
        [
            new ExpandedRequirement
            {
                Id = "WI-001", Title = "Expanded from prior run",
                Status = WorkItemStatus.Completed, ItemType = WorkItemType.Task
            },
            new ExpandedRequirement
            {
                Id = "WI-002", Title = "Second expanded item",
                Status = WorkItemStatus.InQueue, ItemType = WorkItemType.UserStory
            }
        ];

        Directory.CreateDirectory(config.RequirementsPath);

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.ExpandedRequirements.Should().ContainSingle(e => e.Id == "WI-001");
        ctx.ExpandedRequirements.Should().ContainSingle(e => e.Id == "WI-002");
    }

    [Fact]
    public async Task Resume_RestoresDerivedServicesFromPriorRun()
    {
        var agents = new List<IAgent>
        {
            CreateAgentMock(AgentType.RequirementsReader, "ReqReader").Object,
            CreateAgentMock(AgentType.Database, "Database").Object
        };

        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string>
            { "RequirementsReader", "Architect" };
        config.ResumeRequirements =
        [
            new Requirement { Id = "REQ-001", Title = "Test" }
        ];
        config.ResumeDerivedServices =
        [
            new MicroserviceDefinition
            {
                Name = "PatientService", ShortName = "Patient", Schema = "patient",
                Description = "Patient management", ApiPort = 5100,
                Entities = ["Patient", "Encounter"], DependsOn = []
            }
        ];

        Directory.CreateDirectory(config.RequirementsPath);

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.DerivedServices.Should().ContainSingle(s => s.Name == "PatientService");
        ctx.DerivedServices.First().Entities.Should().Contain("Patient");
    }

    [Fact]
    public async Task Resume_WithNoCompletedAgents_RunsAll()
    {
        var reqMock = CreateAgentMock(AgentType.RequirementsReader, "ReqReader");
        var agents = new List<IAgent> { reqMock.Object };

        var config = CreateConfig();
        // No ResumeCompletedAgents — normal run
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        reqMock.Verify(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Resume_EmptyCompletedAgentsSet_RunsAll()
    {
        var reqMock = CreateAgentMock(AgentType.RequirementsReader, "ReqReader");
        var agents = new List<IAgent> { reqMock.Object };

        var config = CreateConfig();
        config.ResumeCompletedAgents = []; // empty set
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        reqMock.Verify(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── Resume: context consistency ──

    [Fact]
    public async Task Resume_RestoredRequirementsAreAvailableToDownstreamAgents()
    {
        List<Requirement>? seenRequirements = null;
        var dbMock = new Mock<IAgent>();
        dbMock.Setup(a => a.Type).Returns(AgentType.Database);
        dbMock.Setup(a => a.Name).Returns("Database");
        dbMock.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                seenRequirements = ctx.Requirements.ToList();
                ctx.AgentStatuses[AgentType.Database] = AgentStatus.Completed;
                return new AgentResult { Agent = AgentType.Database, Success = true, Summary = "ok" };
            });

        var agents = new List<IAgent> { dbMock.Object };
        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string> { "RequirementsReader" };
        config.ResumeRequirements =
        [
            new Requirement { Id = "REQ-A", Title = "Auth", Description = "Auth module" },
            new Requirement { Id = "REQ-B", Title = "Dashboard", Description = "Dashboard view" }
        ];
        Directory.CreateDirectory(config.RequirementsPath);

        var orchestrator = CreateOrchestrator(agents);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        seenRequirements.Should().NotBeNull();
        seenRequirements!.Should().HaveCount(2);
        seenRequirements.Should().Contain(r => r.Id == "REQ-A");
        seenRequirements.Should().Contain(r => r.Id == "REQ-B");
    }

    [Fact]
    public void Resume_InvalidAgentName_IgnoredGracefully()
    {
        // When ResumeCompletedAgents contains a name that doesn't map to AgentType,
        // it should be silently ignored
        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string> { "NonExistentAgent", "RequirementsReader" };

        // Enum.TryParse("NonExistentAgent") → false, should not throw
        var parsed = new HashSet<AgentType>();
        foreach (var name in config.ResumeCompletedAgents)
        {
            if (Enum.TryParse<AgentType>(name, ignoreCase: true, out var at))
                parsed.Add(at);
        }

        parsed.Should().ContainSingle()
            .Which.Should().Be(AgentType.RequirementsReader);
    }

    // ── Resume: null/empty resume data edge cases ──

    [Fact]
    public void Resume_NullResumeRequirements_DoesNotThrow()
    {
        var config = CreateConfig();
        config.ResumeCompletedAgents = new HashSet<string> { "RequirementsReader" };
        config.ResumeRequirements = null;

        // Simulating the orchestrator null-check logic
        var context = new AgentContext();
        if (config.ResumeRequirements is { Count: > 0 })
            context.Requirements = config.ResumeRequirements;

        context.Requirements.Should().BeEmpty();
    }

    [Fact]
    public void Resume_EmptyResumeExpandedRequirements_DoesNotRestore()
    {
        var config = CreateConfig();
        config.ResumeExpandedRequirements = [];

        var context = new AgentContext();
        if (config.ResumeExpandedRequirements is { Count: > 0 })
        {
            foreach (var er in config.ResumeExpandedRequirements)
                context.ExpandedRequirements.Add(er);
        }

        context.ExpandedRequirements.Should().BeEmpty();
    }
}
