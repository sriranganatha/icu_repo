using FluentAssertions;
using HmsAgents.Agents.Orchestrator;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace HmsAgents.Tests;

/// <summary>
/// Tests for Phase 9 workflow execution engine and project pipeline orchestration.
/// </summary>
public class WorkflowAndProjectPipelineTests
{
    // ── AgentContext project-scoped fields ──

    [Fact]
    public void AgentContext_ProjectScoped_DefaultsNull()
    {
        var ctx = new AgentContext();
        ctx.ProjectId.Should().BeNull();
        ctx.ResolvedTechStack.Should().BeEmpty();
        ctx.AgentConfigOverrides.Should().BeEmpty();
        ctx.WorkflowId.Should().BeNull();
        ctx.ResolvedStages.Should().BeEmpty();
    }

    [Fact]
    public void AgentContext_WithProjectId()
    {
        var ctx = new AgentContext
        {
            ProjectId = "proj-001",
            WorkflowId = "wf-001"
        };
        ctx.ProjectId.Should().Be("proj-001");
        ctx.WorkflowId.Should().Be("wf-001");
    }

    [Fact]
    public void AgentContext_ResolvedTechStack()
    {
        var ctx = new AgentContext
        {
            ProjectId = "proj-001",
            ResolvedTechStack =
            [
                new ResolvedTechStackEntry
                {
                    TechnologyId = "csharp",
                    TechnologyName = "C#",
                    TechnologyType = "language",
                    Layer = "backend",
                    Version = "12"
                },
                new ResolvedTechStackEntry
                {
                    TechnologyId = "aspnet",
                    TechnologyName = "ASP.NET Core",
                    TechnologyType = "framework",
                    Layer = "backend",
                    Version = "9.0"
                }
            ]
        };

        ctx.ResolvedTechStack.Should().HaveCount(2);
        ctx.ResolvedTechStack[0].TechnologyName.Should().Be("C#");
    }

    [Fact]
    public void AgentContext_AgentConfigOverrides()
    {
        var ctx = new AgentContext
        {
            ProjectId = "proj-001",
            AgentConfigOverrides = new()
            {
                [AgentType.Database] = new ProjectAgentConfig
                {
                    AgentType = AgentType.Database,
                    LlmModelId = "gemini-2.5-pro",
                    TemperatureOverride = 0.3
                }
            }
        };

        ctx.AgentConfigOverrides.Should().ContainKey(AgentType.Database);
        ctx.AgentConfigOverrides[AgentType.Database].TemperatureOverride.Should().Be(0.3);
    }

    // ── ResolvedStage model ──

    [Fact]
    public void ResolvedStage_BasicProperties()
    {
        var stage = new ResolvedStage
        {
            StageId = "s1",
            Name = "Code Generation",
            Order = 3,
            AgentsInvolved = [AgentType.Database, AgentType.ServiceLayer, AgentType.Application],
            EntryCriteria = "{\"requires_agents\":[\"Architect\"]}",
            ExitCriteria = "{\"all_tests_pass\":true}"
        };

        stage.AgentsInvolved.Should().HaveCount(3);
        stage.ApprovalGate.Should().BeNull();
    }

    [Fact]
    public void ResolvedStage_WithApprovalGate()
    {
        var stage = new ResolvedStage
        {
            StageId = "s2",
            Name = "Deploy",
            Order = 8,
            AgentsInvolved = [AgentType.Deploy],
            ApprovalGate = new ResolvedApprovalGate
            {
                GateType = "human",
                TimeoutHours = 48
            }
        };

        stage.ApprovalGate.Should().NotBeNull();
        stage.ApprovalGate!.GateType.Should().Be("human");
        stage.ApprovalGate.TimeoutHours.Should().Be(48);
    }

    // ── IAgentOrchestrator interface ──

    [Fact]
    public void IAgentOrchestrator_HasRunProjectPipelineAsync()
    {
        typeof(IAgentOrchestrator).GetMethod("RunProjectPipelineAsync").Should().NotBeNull();
        typeof(IAgentOrchestrator).GetMethod("GetProjectContext").Should().NotBeNull();
        typeof(IAgentOrchestrator).GetMethod("GetActiveContexts").Should().NotBeNull();
    }

    // ── AgentOrchestrator concurrent contexts ──

    [Fact]
    public void AgentOrchestrator_GetActiveContexts_InitiallyEmpty()
    {
        var orchestrator = CreateOrchestrator([]);
        orchestrator.GetActiveContexts().Should().BeEmpty();
    }

    [Fact]
    public void AgentOrchestrator_GetProjectContext_ReturnsNull()
    {
        var orchestrator = CreateOrchestrator([]);
        orchestrator.GetProjectContext("nonexistent").Should().BeNull();
    }

    // ── ProjectAgentConfig model ──

    [Fact]
    public void ProjectAgentConfig_AllProperties()
    {
        var config = new ProjectAgentConfig
        {
            AgentType = AgentType.Review,
            LlmModelId = "gpt-4o",
            SystemPromptOverride = "You are a strict code reviewer.",
            TemperatureOverride = 0.1,
            MaxTokensOverride = 16384,
            ConstraintsJson = "{\"max_retries\":5}"
        };

        config.AgentType.Should().Be(AgentType.Review);
        config.LlmModelId.Should().Be("gpt-4o");
        config.MaxTokensOverride.Should().Be(16384);
    }

    // ── ResolvedTechStackEntry model ──

    [Fact]
    public void ResolvedTechStackEntry_Defaults()
    {
        var entry = new ResolvedTechStackEntry();
        entry.TechnologyId.Should().BeEmpty();
        entry.TechnologyName.Should().BeEmpty();
        entry.TechnologyType.Should().BeEmpty();
        entry.Layer.Should().BeEmpty();
        entry.Version.Should().BeEmpty();
        entry.ConfigOverridesJson.Should().BeNull();
    }

    // ── ResolvedApprovalGate model ──

    [Fact]
    public void ResolvedApprovalGate_Defaults()
    {
        var gate = new ResolvedApprovalGate();
        gate.GateType.Should().Be("auto");
        gate.TimeoutHours.Should().Be(24);
        gate.ApproversConfigJson.Should().BeNull();
    }

    // ── Helper ──

    private static AgentOrchestrator CreateOrchestrator(List<IAgent> agents)
    {
        return new AgentOrchestrator(
            agents,
            new Mock<IArtifactWriter>().Object,
            new Mock<IPipelineEventSink>().Object,
            new Mock<IAuditLogger>().Object,
            new Mock<IHumanGate>().Object,
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<AgentOrchestrator>>().Object);
    }
}
