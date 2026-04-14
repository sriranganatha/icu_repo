using FluentAssertions;
using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Edge-case and error-path tests — cancellation, concurrency, empty inputs,
/// null guards, agent failures, and boundary conditions.
/// </summary>
public class EdgeCaseAndErrorPathTests
{
    private readonly Mock<IArtifactWriter> _writerMock = new();
    private readonly Mock<IPipelineEventSink> _sinkMock = new();
    private readonly Mock<IAuditLogger> _auditMock = new();
    private readonly Mock<ILlmProvider> _llmMock = new();
    private readonly Mock<IHumanGate> _humanGateMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _loggerMock = new();

    public EdgeCaseAndErrorPathTests()
    {
        _llmMock.SetupGet(x => x.IsAvailable).Returns(true);
        _llmMock.SetupGet(x => x.ProviderName).Returns("test-llm");
        _llmMock
            .Setup(x => x.GenerateAsync(It.IsAny<LlmPrompt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Success = true, Content = "ok", Model = "m" });
    }

    private PipelineConfig CreateConfig() => new()
    {
        RequirementsPath = Path.Combine(Path.GetTempPath(), $"gnex-edge-{Guid.NewGuid():N}"),
        OutputPath = Path.Combine(Path.GetTempPath(), $"gnex-out-{Guid.NewGuid():N}"),
        SpinUpDocker = false, ExecuteDdl = false
    };

    private AgentOrchestrator CreateOrchestrator(List<IAgent> agents)
        => new(agents, _llmMock.Object, _writerMock.Object, _sinkMock.Object,
            _auditMock.Object, _humanGateMock.Object, _serviceProviderMock.Object, _loggerMock.Object);

    // ── Context initialization ──

    [Fact]
    public void AgentContext_NewInstance_HasUniqueRunId()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => new AgentContext().RunId).ToHashSet();
        ids.Should().HaveCount(100);
    }

    [Fact]
    public void AgentContext_NewInstance_AllCollectionsEmpty()
    {
        var ctx = new AgentContext();
        ctx.Requirements.Should().BeEmpty();
        ctx.ExpandedRequirements.Should().BeEmpty();
        ctx.Artifacts.Should().BeEmpty();
        ctx.Findings.Should().BeEmpty();
        ctx.Messages.Should().BeEmpty();
        ctx.TestDiagnostics.Should().BeEmpty();
        ctx.DerivedServices.Should().BeEmpty();
        ctx.LearningRecords.Should().BeEmpty();
        ctx.QualityMetrics.Should().BeEmpty();
        ctx.FailureRecords.Should().BeEmpty();
        ctx.RequirementVersions.Should().BeEmpty();
        ctx.BrdDocuments.Should().BeEmpty();
        ctx.Checkpoints.Should().BeEmpty();
        ctx.ArtifactConflicts.Should().BeEmpty();
        ctx.SprintPlans.Should().BeEmpty();
        ctx.CommunicationLog.Should().BeEmpty();
    }

    [Fact]
    public void AgentContext_StartedAt_IsRecentUtc()
    {
        var ctx = new AgentContext();
        ctx.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AgentContext_CompletedAt_DefaultNull()
    {
        var ctx = new AgentContext();
        ctx.CompletedAt.Should().BeNull();
    }

    // ── Agent orchestrator: empty agents ──

    [Fact]
    public async Task Pipeline_NoAgents_CompletesGracefully()
    {
        var orchestrator = CreateOrchestrator([]);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.Should().NotBeNull();
        ctx.CompletedAt.Should().NotBeNull();
    }

    // ── Agent failure handling ──

    [Fact]
    public async Task Pipeline_AgentFails_ContinuesRemaining()
    {
        var failingAgent = new Mock<IAgent>();
        failingAgent.Setup(a => a.Type).Returns(AgentType.RequirementsReader);
        failingAgent.Setup(a => a.Name).Returns("FailingReader");
        failingAgent.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentContext ctx, CancellationToken _) =>
            {
                ctx.AgentStatuses[AgentType.RequirementsReader] = AgentStatus.Failed;
                return new AgentResult
                    { Agent = AgentType.RequirementsReader, Success = false, Summary = "Failed" };
            });

        var agents = new List<IAgent> { failingAgent.Object };
        var orchestrator = CreateOrchestrator(agents);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ-001\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);

        ctx.AgentStatuses.Should().ContainKey(AgentType.RequirementsReader);
        ctx.AgentStatuses[AgentType.RequirementsReader].Should().Be(AgentStatus.Failed);
    }

    [Fact]
    public async Task Pipeline_AgentThrowsException_PipelineDoesNotCrash()
    {
        var throwingAgent = new Mock<IAgent>();
        throwingAgent.Setup(a => a.Type).Returns(AgentType.RequirementsReader);
        throwingAgent.Setup(a => a.Name).Returns("Thrower");
        throwingAgent.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Agent internal error"));

        var agents = new List<IAgent> { throwingAgent.Object };
        var orchestrator = CreateOrchestrator(agents);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Pipeline should handle the exception internally (retry or fail gracefully)
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);
        ctx.Should().NotBeNull();
    }

    // ── Cancellation ──

    [Fact]
    public async Task Pipeline_CancellationRequested_StopsGracefully()
    {
        var slowAgent = new Mock<IAgent>();
        slowAgent.Setup(a => a.Type).Returns(AgentType.RequirementsReader);
        slowAgent.Setup(a => a.Name).Returns("SlowAgent");
        slowAgent.Setup(a => a.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (AgentContext ctx, CancellationToken ct) =>
            {
                await Task.Delay(30_000, ct); // Will be cancelled
                return new AgentResult { Agent = AgentType.RequirementsReader, Success = true };
            });

        var orchestrator = CreateOrchestrator([slowAgent.Object]);
        var config = CreateConfig();
        Directory.CreateDirectory(config.RequirementsPath);
        File.WriteAllText(Path.Combine(config.RequirementsPath, "test.md"), "# REQ\nTest");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Should complete (with cancellation) without throwing
        var ctx = await orchestrator.RunPipelineAsync(config, cts.Token);
        ctx.Should().NotBeNull();
    }

    // ── Null/empty edges in models ──

    [Fact]
    public void Requirement_DefaultValues()
    {
        var r = new Requirement();
        r.Id.Should().BeEmpty();
        r.SourceFile.Should().BeEmpty();
        r.Title.Should().BeEmpty();
        r.Description.Should().BeEmpty();
        r.Tags.Should().BeEmpty();
        r.AcceptanceCriteria.Should().BeEmpty();
        r.DependsOn.Should().BeEmpty();
        r.ProjectId.Should().BeNull();
    }

    [Fact]
    public void ReviewFinding_DefaultValues()
    {
        var f = new ReviewFinding();
        f.Id.Should().NotBeNullOrEmpty();
        f.ArtifactId.Should().BeEmpty();
        f.Category.Should().BeEmpty();
        f.Message.Should().BeEmpty();
        f.ProjectId.Should().BeNull();
    }

    [Fact]
    public void TestDiagnostic_DefaultValues()
    {
        var d = new TestDiagnostic();
        d.Id.Should().NotBeNullOrEmpty();
        d.AttemptNumber.Should().Be(1);
        d.ProjectId.Should().BeNull();
        d.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AgentResult_DefaultValues()
    {
        var r = new AgentResult();
        r.Success.Should().BeFalse();
        r.Summary.Should().BeEmpty();
        r.Artifacts.Should().BeEmpty();
    }

    // ── Agent type and status enums ──

    [Fact]
    public void AgentType_ContainsAllExpectedValues()
    {
        var values = Enum.GetValues<AgentType>();
        values.Should().Contain(AgentType.RequirementsReader);
        values.Should().Contain(AgentType.Database);
        values.Should().Contain(AgentType.ServiceLayer);
        values.Should().Contain(AgentType.Application);
        values.Should().Contain(AgentType.Integration);
        values.Should().Contain(AgentType.Testing);
        values.Should().Contain(AgentType.Review);
        values.Should().Contain(AgentType.Supervisor);
        values.Should().Contain(AgentType.BugFix);
        values.Should().Contain(AgentType.Security);
        values.Should().Contain(AgentType.Deploy);
        values.Should().Contain(AgentType.Architect);
        values.Should().Contain(AgentType.PromptGenerator);
        values.Should().Contain(AgentType.DodVerification);
        values.Should().Contain(AgentType.ConflictResolver);
        values.Should().Contain(AgentType.TraceabilityGate);
        values.Should().Contain(AgentType.SprintPlanner);
        values.Should().Contain(AgentType.LearningLoop);
    }

    [Fact]
    public void ArtifactLayer_ContainsAllExpectedValues()
    {
        var layers = Enum.GetValues<ArtifactLayer>();
        layers.Should().Contain(ArtifactLayer.Database);
        layers.Should().Contain(ArtifactLayer.Service);
        layers.Should().Contain(ArtifactLayer.Test);
        layers.Should().Contain(ArtifactLayer.Infrastructure);
        layers.Should().Contain(ArtifactLayer.Security);
        layers.Should().Contain(ArtifactLayer.Compliance);
    }

    [Fact]
    public void ReviewSeverity_ContainsAllLevels()
    {
        var levels = Enum.GetValues<ReviewSeverity>();
        levels.Should().Contain(ReviewSeverity.Info);
        levels.Should().Contain(ReviewSeverity.Warning);
        levels.Should().Contain(ReviewSeverity.Error);
        levels.Should().Contain(ReviewSeverity.Critical);
        levels.Should().Contain(ReviewSeverity.SecurityViolation);
        levels.Should().Contain(ReviewSeverity.ComplianceViolation);
    }

    // ── Concurrent agent operations ──

    [Fact]
    public void AgentContext_ConcurrentStatusUpdates_AllRecorded()
    {
        var ctx = new AgentContext();
        var agents = Enum.GetValues<AgentType>();

        Parallel.ForEach(agents, at =>
        {
            ctx.AgentStatuses[at] = AgentStatus.Running;
        });

        Parallel.ForEach(agents, at =>
        {
            ctx.AgentStatuses[at] = AgentStatus.Completed;
        });

        foreach (var at in agents)
            ctx.AgentStatuses[at].Should().Be(AgentStatus.Completed);
    }

    [Fact]
    public void AgentContext_ConcurrentFindingsAdd_ThreadSafe()
    {
        var ctx = new AgentContext();

        Parallel.For(0, 200, i =>
        {
            ctx.Findings.Add(new ReviewFinding
            {
                Category = "Test",
                FilePath = $"File{i}.cs",
                Message = $"Finding {i}"
            });
        });

        ctx.Findings.Should().HaveCount(200);
    }

    [Fact]
    public void AgentContext_DirectiveQueue_ConcurrentEnqueue()
    {
        var ctx = new AgentContext();

        Parallel.For(0, 50, i =>
        {
            ctx.DirectiveQueue.Enqueue(new AgentDirective
            {
                From = AgentType.Review,
                To = AgentType.BugFix,
                Action = $"Fix issue {i}"
            });
        });

        ctx.DirectiveQueue.Should().HaveCount(50);
    }

    // ── Orchestrator context management ──

    [Fact]
    public void Orchestrator_ResetContext_NullsTheContext()
    {
        var orchestrator = CreateOrchestrator([]);
        orchestrator.GetCurrentContext().Should().BeNull();
        orchestrator.ResetContext();
        orchestrator.GetCurrentContext().Should().BeNull();
    }

    [Fact]
    public void Orchestrator_GetActiveContexts_InitiallyEmpty()
    {
        var orchestrator = CreateOrchestrator([]);
        orchestrator.GetActiveContexts().Should().BeEmpty();
    }

    [Fact]
    public void Orchestrator_GetProjectContext_Null_WhenNoActiveRun()
    {
        var orchestrator = CreateOrchestrator([]);
        orchestrator.GetProjectContext("nonexistent").Should().BeNull();
    }

    // ── LLM provider mock validation ──

    [Fact]
    public void LlmPrompt_Defaults()
    {
        var prompt = new LlmPrompt();
        prompt.Temperature.Should().Be(0.2);
        prompt.MaxTokens.Should().Be(4096);
        prompt.SystemPrompt.Should().BeEmpty();
        prompt.UserPrompt.Should().BeEmpty();
        prompt.ContextSnippets.Should().BeEmpty();
    }

    [Fact]
    public void LlmResponse_SuccessDefaults()
    {
        var response = new LlmResponse { Success = true, Content = "code" };
        response.Success.Should().BeTrue();
        response.Content.Should().Be("code");
        response.Error.Should().BeNull();
    }

    [Fact]
    public void LlmResponse_FailureWithError()
    {
        var response = new LlmResponse { Success = false, Error = "Rate limit exceeded" };
        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Rate limit");
    }

    // ── PipelineEvent model ──

    [Fact]
    public void PipelineEvent_Timestamp_IsRecentUtc()
    {
        var evt = new PipelineEvent { Agent = AgentType.Database, Status = AgentStatus.Running };
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PipelineEvent_RetryAttempt_Defaults()
    {
        var evt = new PipelineEvent();
        evt.RetryAttempt.Should().Be(0);
    }

    // ── Agent feedback ──

    [Fact]
    public void AgentContext_Feedback_Recording()
    {
        var ctx = new AgentContext();

        ctx.AgentFeedback.GetOrAdd(AgentType.ServiceLayer, _ => [])
            .Add("Missing validation on PatientService.Update");
        ctx.AgentFeedback.GetOrAdd(AgentType.ServiceLayer, _ => [])
            .Add("DTO mapping incomplete");

        ctx.AgentFeedback[AgentType.ServiceLayer].Should().HaveCount(2);
    }

    [Fact]
    public void AgentContext_CommunicationLog_Populated()
    {
        var ctx = new AgentContext();
        ctx.CommunicationLog.Add(new AgentCommunicationEntry
        {
            FromAgent = AgentType.Review,
            ToAgent = AgentType.BugFix,
            CommType = AgentCommType.DispatchFindings,
            Message = "Missing null check"
        });

        ctx.CommunicationLog.Should().HaveCount(1);
    }
}
