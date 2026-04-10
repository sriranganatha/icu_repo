using GNex.Core.Enums;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using AgentType = GNex.Core.Enums.AgentType;

namespace GNex.Agents.Orchestrator;

/// <summary>
/// Enforces SLA policies on agent execution — retries, timeouts, escalation.
/// </summary>
public sealed class SlaEscalationEngine
{
    private readonly ILogger _logger;
    private readonly List<AgentEscalationPolicy> _policies;

    public SlaEscalationEngine(ILogger logger, List<AgentEscalationPolicy> policies)
    {
        _logger = logger;
        _policies = policies;
    }

    public static List<AgentEscalationPolicy> DefaultPolicies =>
    [
        new() { AgentType = nameof(Core.Enums.AgentType.Database), MaxRetries = 3, MaxDuration = TimeSpan.FromMinutes(5), EscalationAfter = TimeSpan.FromMinutes(3), OnExceed = EscalationAction.Retry },
        new() { AgentType = nameof(Core.Enums.AgentType.ServiceLayer), MaxRetries = 3, MaxDuration = TimeSpan.FromMinutes(5), EscalationAfter = TimeSpan.FromMinutes(3), OnExceed = EscalationAction.Retry },
        new() { AgentType = nameof(Core.Enums.AgentType.Testing), MaxRetries = 2, MaxDuration = TimeSpan.FromMinutes(10), EscalationAfter = TimeSpan.FromMinutes(7), OnExceed = EscalationAction.FailAndNotify },
        new() { AgentType = nameof(Core.Enums.AgentType.Review), MaxRetries = 2, MaxDuration = TimeSpan.FromMinutes(5), EscalationAfter = TimeSpan.FromMinutes(4), OnExceed = EscalationAction.Retry },
        new() { AgentType = nameof(Core.Enums.AgentType.Deploy), MaxRetries = 1, MaxDuration = TimeSpan.FromMinutes(15), EscalationAfter = TimeSpan.FromMinutes(10), OnExceed = EscalationAction.HumanEscalation },
        new() { AgentType = nameof(Core.Enums.AgentType.Build), MaxRetries = 2, MaxDuration = TimeSpan.FromMinutes(10), EscalationAfter = TimeSpan.FromMinutes(7), OnExceed = EscalationAction.FailAndNotify },
        new() { AgentType = nameof(Core.Enums.AgentType.BrdGenerator), MaxRetries = 2, MaxDuration = TimeSpan.FromMinutes(3), EscalationAfter = TimeSpan.FromMinutes(2), OnExceed = EscalationAction.Retry },
    ];

    public AgentEscalationPolicy? GetPolicy(AgentType agent) =>
        _policies.FirstOrDefault(p => p.AgentType == agent.ToString());

    /// <summary>
    /// Evaluate whether an agent should be retried, failed, escalated, or skipped.
    /// </summary>
    public EscalationDecision Evaluate(AgentType agent, int currentRetries, TimeSpan elapsed)
    {
        var policy = GetPolicy(agent);
        if (policy is null)
            return new EscalationDecision(EscalationAction.Retry, "No policy — default retry.");

        if (currentRetries >= policy.MaxRetries)
        {
            _logger.LogWarning("Agent {Agent} exceeded max retries ({Max}). Action: {Action}",
                agent, policy.MaxRetries, policy.OnExceed);
            return new EscalationDecision(policy.OnExceed, $"Exceeded max retries ({policy.MaxRetries}).");
        }

        if (elapsed > policy.MaxDuration)
        {
            _logger.LogWarning("Agent {Agent} exceeded max duration ({Max}). Action: {Action}",
                agent, policy.MaxDuration, policy.OnExceed);
            return new EscalationDecision(policy.OnExceed, $"Exceeded max duration ({policy.MaxDuration}).");
        }

        if (elapsed > policy.EscalationAfter)
        {
            _logger.LogInformation("Agent {Agent} approaching SLA limit ({Elapsed}/{Max})",
                agent, elapsed, policy.MaxDuration);
            return new EscalationDecision(EscalationAction.Retry, $"Approaching SLA limit — retrying ({currentRetries + 1}/{policy.MaxRetries}).");
        }

        return new EscalationDecision(EscalationAction.Retry, "Within SLA bounds.");
    }
}

public readonly record struct EscalationDecision(EscalationAction Action, string Reason);
