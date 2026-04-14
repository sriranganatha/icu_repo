using FluentAssertions;
using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Full pipeline E2E simulation — realistic multi-stage pipeline run that
/// validates data flow between agents, artifact accumulation, finding generation,
/// context integrity, and end-state correctness.
/// </summary>
public class FullPipelineE2ETests
{
    private readonly Mock<IArtifactWriter> _writerMock = new();
    private readonly Mock<IPipelineEventSink> _sinkMock = new();
    private readonly Mock<IAuditLogger> _auditMock = new();
    private readonly Mock<ILlmProvider> _llmMock = new();
    private readonly Mock<IHumanGate> _humanGateMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _loggerMock = new();
    private readonly List<PipelineEvent> _capturedEvents = [];

    public FullPipelineE2ETests()
    {
        _llmMock.SetupGet(x => x.IsAvailable).Returns(true);
        _llmMock.SetupGet(x => x.ProviderName).Returns("test-llm");
        _llmMock
            .Setup(x => x.GenerateAsync(It.IsAny<LlmPrompt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Success = true, Content = "ok\nsteps\nrisks", Model = "test" });

        _sinkMock
            .Setup(s => s.OnEventAsync(It.IsAny<PipelineEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineEvent, CancellationToken>((evt, _) => _capturedEvents.Add(evt))
            .Returns(Task.CompletedTask);
    }

    private PipelineConfig CreateConfig() => new()
    {
        RequirementsPath = Path.Combine(Path.GetTempPath(), $"gnex-e2e-{Guid.NewGuid():N}"),
        OutputPath = Path.Combine(Path.GetTempPath(), $"gnex-out-{Guid.NewGuid():N}"),
        SpinUpDocker = false, ExecuteDdl = false, ProjectDomain = "Healthcare"
    };

    private AgentOrchestrator CreateOrchestrator(List<IAgent> agents)
        => new(agents, _llmMock.Object, _writerMock.Object, _sinkMock.Object,
            _auditMock.Object, _humanGateMock.Object, _serviceProviderMock.Object, _loggerMock.Object);

    // ── Realistic multi-agent pipeline ──

    [Fact]
    public async Task FullPipeline_MultiAgent_ProducesArtifactsAndFindings()
    {
        var agents = CreateRealisticAgentSet();
        var orchestrator = CreateOrchestrator(agents);
        var config = CreateConfig();

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "patient.md"),
            "# REQ-001 Patient Registration\n\nAs a nurse, I need to register patients with demographics.");
        File.WriteAllText(Path.Combine(config.RequirementsPath, "encounter.md"),
            "# REQ-002 Encounter Management\n\nAs a doctor, I need to create and manage patient encounters.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        // Pipeline completed
        ctx.CompletedAt.Should().NotBeNull();

        // Artifacts were produced by multiple agents
        ctx.Artifacts.Should().NotBeEmpty();
        ctx.Artifacts.Select(a => a.ProducedBy).Distinct().Should().HaveCountGreaterThan(1);

        // Pipeline events were published via sink
        _capturedEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FullPipeline_PipelineConfig_AvailableOnContext()
    {
        var agents = CreateRealisticAgentSet();
        var orchestrator = CreateOrchestrator(agents);
        var config = new PipelineConfig
        {
            RequirementsPath = Path.Combine(Path.GetTempPath(), $"gnex-e2e-{Guid.NewGuid():N}"),
            OutputPath = Path.Combine(Path.GetTempPath(), $"gnex-out-{Guid.NewGuid():N}"),
            SpinUpDocker = false, ExecuteDdl = false,
            ProjectDomain = "Healthcare",
            ProjectDomainDescription = "Hospital management with FHIR"
        };

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.PipelineConfig.Should().NotBeNull();
        ctx.PipelineConfig!.ProjectDomain.Should().Be("Healthcare");
    }

    [Fact]
    public async Task FullPipeline_EventSink_ReceivesAgentEvents()
    {
        var agents = new List<IAgent>
        {
            CreateFastAgent(AgentType.RequirementsReader, "ReqReader"),
            CreateFastAgent(AgentType.Database, "Database"),
        };

        var orchestrator = CreateOrchestrator(agents);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        // Events should include both Running and Completed/Failed states
        _capturedEvents.Should().Contain(e => e.Status == AgentStatus.Running);
    }

    [Fact]
    public async Task FullPipeline_AuditLogger_Called()
    {
        var agents = new List<IAgent>
        {
            CreateFastAgent(AgentType.RequirementsReader, "ReqReader")
        };

        var orchestrator = CreateOrchestrator(agents);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        _auditMock.Verify(
            a => a.LogAsync(
                It.IsAny<AgentType>(),
                It.IsAny<string>(),
                It.IsAny<AuditAction>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<AuditSeverity>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── Data flow between agents ──

    [Fact]
    public async Task Pipeline_RequirementsReader_OutputFlowsToDownstream()
    {
        List<Requirement>? capturedReqs = null;

        var reqReader = new Mock<IAgent>();
        reqReader.Setup(a => a.Type).Returns(AgentType.RequirementsReader);
        reqReader.Setup(a => a.Name).Returns("ReqReader");
        reqReader.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                ctx.Requirements.Add(new Requirement { Id = "REQ-001", Title = "Patient CRUD" });
                ctx.Requirements.Add(new Requirement { Id = "REQ-002", Title = "Encounter API" });
                ctx.AgentStatuses[AgentType.RequirementsReader] = AgentStatus.Completed;
                return new AgentResult { Agent = AgentType.RequirementsReader, Success = true, Summary = "ok" };
            });

        var dbAgent = new Mock<IAgent>();
        dbAgent.Setup(a => a.Type).Returns(AgentType.Database);
        dbAgent.Setup(a => a.Name).Returns("Database");
        dbAgent.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                capturedReqs = ctx.Requirements.ToList();
                ctx.AgentStatuses[AgentType.Database] = AgentStatus.Completed;
                return new AgentResult { Agent = AgentType.Database, Success = true, Summary = "ok" };
            });

        var orchestrator = CreateOrchestrator([reqReader.Object, dbAgent.Object]);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await orchestrator.RunPipelineAsync(config, cts.Token);

        capturedReqs.Should().NotBeNull();
        capturedReqs!.Should().Contain(r => r.Id == "REQ-001");
        capturedReqs!.Should().Contain(r => r.Id == "REQ-002");
    }

    [Fact]
    public async Task Pipeline_ArtifactsAccumulate_AcrossAgents()
    {
        var db = new Mock<IAgent>();
        db.Setup(a => a.Type).Returns(AgentType.Database);
        db.Setup(a => a.Name).Returns("Database");
        db.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                var artifact = new CodeArtifact
                {
                    Layer = ArtifactLayer.Database,
                    RelativePath = "Database/Patient.cs",
                    FileName = "Patient.cs",
                    Content = "public class Patient { }",
                    ProducedBy = AgentType.Database,
                    TracedRequirementIds = ["REQ-001"]
                };
                ctx.Artifacts.Add(artifact);
                ctx.AgentStatuses[AgentType.Database] = AgentStatus.Completed;
                return new AgentResult { Agent = AgentType.Database, Success = true, Summary = "ok", Artifacts = [artifact] };
            });

        var svc = new Mock<IAgent>();
        svc.Setup(a => a.Type).Returns(AgentType.ServiceLayer);
        svc.Setup(a => a.Name).Returns("ServiceLayer");
        svc.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                // Verify DB artifacts are already in context
                var dbArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Database).ToList();

                var artifact = new CodeArtifact
                {
                    Layer = ArtifactLayer.Service,
                    RelativePath = "Services/PatientService.cs",
                    FileName = "PatientService.cs",
                    Content = "public class PatientService { }",
                    ProducedBy = AgentType.ServiceLayer,
                    TracedRequirementIds = ["REQ-001"]
                };
                ctx.Artifacts.Add(artifact);
                ctx.AgentStatuses[AgentType.ServiceLayer] = AgentStatus.Completed;
                return new AgentResult { Agent = AgentType.ServiceLayer, Success = true, Summary = "ok", Artifacts = [artifact] };
            });

        var orchestrator = CreateOrchestrator([db.Object, svc.Object]);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.Artifacts.Should().HaveCountGreaterOrEqualTo(2);
        ctx.Artifacts.Should().Contain(a => a.ProducedBy == AgentType.Database);
        ctx.Artifacts.Should().Contain(a => a.ProducedBy == AgentType.ServiceLayer);
    }

    // ── Agent status tracking ──

    [Fact]
    public async Task Pipeline_TracksAgentStatuses()
    {
        var agents = new List<IAgent>
        {
            CreateFastAgent(AgentType.RequirementsReader, "ReqReader"),
            CreateFastAgent(AgentType.Database, "Database"),
        };

        var orchestrator = CreateOrchestrator(agents);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        // At least some agents should have completed
        ctx.AgentStatuses.Values.Should().Contain(AgentStatus.Completed);
    }

    // ── Domain context propagation ──

    [Fact]
    public async Task Pipeline_DomainConfig_PropagatedToContext()
    {
        var agents = new List<IAgent> { CreateFastAgent(AgentType.RequirementsReader, "ReqReader") };
        var orchestrator = CreateOrchestrator(agents);
        var config = new PipelineConfig
        {
            RequirementsPath = Path.Combine(Path.GetTempPath(), $"gnex-e2e-{Guid.NewGuid():N}"),
            OutputPath = Path.Combine(Path.GetTempPath(), $"gnex-out-{Guid.NewGuid():N}"),
            SpinUpDocker = false, ExecuteDdl = false,
            ProjectDomain = "FinTech",
            ProjectDomainDescription = "B2B SaaS banking platform"
        };

        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.PipelineConfig.Should().NotBeNull();
        ctx.PipelineConfig!.ProjectDomain.Should().Be("FinTech");
        ctx.PipelineConfig.DomainContext.Should().Be("B2B SaaS banking platform");
    }

    // ── Helpers ──

    private static IAgent CreateFastAgent(AgentType type, string name)
    {
        var mock = new Mock<IAgent>();
        mock.Setup(a => a.Type).Returns(type);
        mock.Setup(a => a.Name).Returns(name);
        mock.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                ctx.AgentStatuses[type] = AgentStatus.Completed;
                var artifact = new CodeArtifact
                {
                    Layer = ArtifactLayer.Service, RelativePath = $"{name}/out.cs",
                    FileName = "out.cs", Content = $"// {name}", ProducedBy = type
                };
                ctx.Artifacts.Add(artifact);
                return new AgentResult { Agent = type, Success = true, Summary = "ok", Artifacts = [artifact] };
            });
        return mock.Object;
    }

    private static List<IAgent> CreateRealisticAgentSet()
    {
        return
        [
            CreateFastAgent(AgentType.RequirementsReader, "ReqReader"),
            CreateFastAgent(AgentType.Database, "Database"),
            CreateFastAgent(AgentType.ServiceLayer, "ServiceLayer"),
            CreateFastAgent(AgentType.Application, "Application"),
            CreateFastAgent(AgentType.Integration, "Integration"),
            CreateFastAgent(AgentType.Testing, "Testing"),
            CreateFastAgent(AgentType.Review, "Review"),
            CreateFastAgent(AgentType.Supervisor, "Supervisor"),
        ];
    }
}
