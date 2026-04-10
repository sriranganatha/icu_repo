using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

public class SlaEscalationTests
{
    private static SlaEscalationEngine CreateEngine() =>
        new(new Microsoft.Extensions.Logging.Abstractions.NullLogger<SlaEscalationEngine>(),
            SlaEscalationEngine.DefaultPolicies);

    [Fact]
    public void DefaultPolicies_ContainsExpectedAgents()
    {
        var policies = SlaEscalationEngine.DefaultPolicies;
        Assert.Contains(policies, p => p.AgentType == nameof(AgentType.Database));
        Assert.Contains(policies, p => p.AgentType == nameof(AgentType.Deploy));
    }

    [Fact]
    public void Evaluate_WithinBounds_ReturnsRetry()
    {
        var engine = CreateEngine();
        var decision = engine.Evaluate(AgentType.Database, 0, TimeSpan.FromSeconds(30));

        Assert.Equal(EscalationAction.Retry, decision.Action);
    }

    [Fact]
    public void Evaluate_ExceedsMaxRetries_ReturnsConfiguredAction()
    {
        var engine = CreateEngine();
        // Database policy: MaxRetries = 3, OnExceed = Retry
        var decision = engine.Evaluate(AgentType.Database, 3, TimeSpan.FromSeconds(30));

        Assert.Equal(EscalationAction.Retry, decision.Action);
        Assert.Contains("max retries", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ExceedsMaxDuration_ReturnsConfiguredAction()
    {
        var engine = CreateEngine();
        var decision = engine.Evaluate(AgentType.Database, 0, TimeSpan.FromMinutes(10));

        Assert.Equal(EscalationAction.Retry, decision.Action);
        Assert.Contains("max duration", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Deploy_ExceedsRetries_EscalatesToHuman()
    {
        var engine = CreateEngine();
        // Deploy policy: MaxRetries = 1, OnExceed = HumanEscalation
        var decision = engine.Evaluate(AgentType.Deploy, 1, TimeSpan.FromSeconds(30));

        Assert.Equal(EscalationAction.HumanEscalation, decision.Action);
    }

    [Fact]
    public void Evaluate_NoPolicyAgent_ReturnsDefaultRetry()
    {
        var engine = CreateEngine();
        var decision = engine.Evaluate(AgentType.Supervisor, 5, TimeSpan.FromMinutes(60));

        Assert.Equal(EscalationAction.Retry, decision.Action);
        Assert.Contains("No policy", decision.Reason);
    }

    [Fact]
    public void Evaluate_ApproachingEscalation_StillRetries()
    {
        var engine = CreateEngine();
        // Database: EscalationAfter = 3min
        var decision = engine.Evaluate(AgentType.Database, 0, TimeSpan.FromMinutes(3.5));

        Assert.Equal(EscalationAction.Retry, decision.Action);
        Assert.Contains("Approaching", decision.Reason);
    }
}
