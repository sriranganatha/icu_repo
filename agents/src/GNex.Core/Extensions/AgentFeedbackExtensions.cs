using System.Collections.Concurrent;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Core.Extensions;

/// <summary>
/// Extension methods for the AgentFeedback system on AgentContext.
/// Writers: Review, Supervisor, GapAnalysis, Monitor, Build — write targeted feedback keyed by target agent.
/// Readers: ServiceLayer, Testing, Application, Database, Integration, BugFix — read feedback addressed to them.
/// </summary>
public static class AgentFeedbackExtensions
{
    /// <summary>
    /// Write a feedback note targeting a specific agent.
    /// The feedback is keyed by the TARGET agent type — so the target can read it later.
    /// </summary>
    public static void WriteFeedback(this AgentContext context, AgentType targetAgent, AgentType fromAgent, string message)
    {
        var bag = context.AgentFeedback.GetOrAdd(targetAgent, _ => new ConcurrentBag<string>());
        bag.Add($"[{fromAgent}] {message}");

        if (context.PipelineConfig?.EnableAgentCommunicationLogging == true)
        {
            context.CommunicationLog.Add(new AgentCommunicationEntry
            {
                RunId = context.RunId,
                ProjectId = context.ProjectId,
                CommType = AgentCommType.WriteFeedback,
                FromAgent = fromAgent,
                ToAgent = targetAgent,
                Message = message.Length > 500 ? message[..500] : message,
                ItemCount = 1
            });
        }
    }

    /// <summary>
    /// Read all feedback notes addressed to a specific agent.
    /// Returns a flat list of feedback strings from all sources.
    /// </summary>
    public static List<string> ReadFeedback(this AgentContext context, AgentType forAgent)
    {
        var items = context.AgentFeedback.TryGetValue(forAgent, out var bag)
            ? bag.ToList()
            : [];

        if (context.PipelineConfig?.EnableAgentCommunicationLogging == true && items.Count > 0)
        {
            context.CommunicationLog.Add(new AgentCommunicationEntry
            {
                RunId = context.RunId,
                ProjectId = context.ProjectId,
                CommType = AgentCommType.ReadFeedback,
                FromAgent = forAgent,
                ToAgent = null,
                Message = $"Read {items.Count} feedback items",
                ItemCount = items.Count
            });
        }

        return items;
    }

    /// <summary>
    /// Read feedback addressed to an agent and format it as a single block
    /// suitable for injecting into prompts or template comments.
    /// Returns empty string if no feedback exists.
    /// </summary>
    public static string ReadFeedbackBlock(this AgentContext context, AgentType forAgent, int maxItems = 10)
    {
        var items = ReadFeedback(context, forAgent);
        if (items.Count == 0) return string.Empty;

        var selected = items.Take(maxItems).ToList();
        var header = $"// ── Feedback from previous iterations ({selected.Count}/{items.Count} items) ──";
        var lines = string.Join("\n// ", selected);
        return $"{header}\n// {lines}";
    }

    /// <summary>
    /// Get count of feedback items for a specific agent.
    /// </summary>
    public static int FeedbackCount(this AgentContext context, AgentType forAgent)
    {
        return context.AgentFeedback.TryGetValue(forAgent, out var bag) ? bag.Count : 0;
    }

    /// <summary>
    /// Write categorized feedback notes from review findings to the relevant target agents.
    /// Maps finding categories to the agents responsible for fixing them.
    /// </summary>
    public static void DispatchFindingsAsFeedback(this AgentContext context, AgentType fromAgent, IEnumerable<ReviewFinding> findings)
    {
        var dispatchedCount = 0;
        foreach (var finding in findings.Where(f => f.Severity >= ReviewSeverity.Warning))
        {
            var targets = MapFindingToTargetAgents(finding.Category);
            foreach (var target in targets)
            {
                context.WriteFeedback(target, fromAgent, $"[{finding.Category}] {finding.Message}");
                dispatchedCount++;
            }
        }

        if (context.PipelineConfig?.EnableAgentCommunicationLogging == true && dispatchedCount > 0)
        {
            context.CommunicationLog.Add(new AgentCommunicationEntry
            {
                RunId = context.RunId,
                ProjectId = context.ProjectId,
                CommType = AgentCommType.DispatchFindings,
                FromAgent = fromAgent,
                ToAgent = null,
                Message = $"Dispatched {dispatchedCount} finding-feedback messages",
                ItemCount = dispatchedCount
            });
        }
    }

    private static AgentType[] MapFindingToTargetAgents(string category) => category switch
    {
        "NFR-CODE-01" or "NFR-CODE-02" or "Implementation" => [AgentType.ServiceLayer, AgentType.BugFix],
        "NFR-TEST-01" or "TestCoverage" => [AgentType.Testing, AgentType.BugFix],
        "MultiTenant" or "Audit" => [AgentType.Database, AgentType.ServiceLayer, AgentType.BugFix],
        "Security" => [AgentType.Database, AgentType.ServiceLayer, AgentType.Application],
        "Coverage" or "FeatureCoverage" => [AgentType.ServiceLayer, AgentType.Testing, AgentType.Application],
        "Traceability" or "Conventions" => [AgentType.ServiceLayer, AgentType.Application],
        "Deployment" or "Runtime" => [AgentType.Application, AgentType.BugFix],
        "GapAnalysis" => [AgentType.Database, AgentType.ServiceLayer, AgentType.Testing, AgentType.Integration],
        _ => [AgentType.BugFix]
    };

    // ── Centralized LLM context builder ──────────────────────────

    /// <summary>
    /// Builds a comprehensive prompt context block from feedback, DomainProfile,
    /// QualityMetrics, and AgentResults. Agents inject this into their LLM prompts
    /// so the AI has full awareness of domain context and previous iterations.
    /// </summary>
    public static string BuildLlmContextBlock(this AgentContext context, AgentType forAgent, int maxFeedback = 10)
    {
        var sections = new List<string>();

        // 1. Project tech stack
        var techStackSummary = context.BuildTechStackSummary();
        if (!string.IsNullOrWhiteSpace(techStackSummary))
            sections.Add(techStackSummary);

        // 2. Agent-specific custom system prompt from DomainProfile
        var profile = context.DomainProfile;
        if (profile?.AgentPrompts.TryGetValue(forAgent.ToString(), out var agentPrompt) == true
            && !string.IsNullOrWhiteSpace(agentPrompt))
        {
            sections.Add($"## Domain-Specific Instructions for {forAgent}\n{agentPrompt}");
        }

        // 3. Domain context (cont.)
        if (profile is not null)
        {
            var domainLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(profile.Domain))
                domainLines.Add($"Domain: {profile.Domain}");
            if (!string.IsNullOrWhiteSpace(profile.DomainDescription))
                domainLines.Add($"Description: {profile.DomainDescription}");
            if (profile.ComplianceFrameworks.Count > 0)
                domainLines.Add($"Compliance: {string.Join(", ", profile.ComplianceFrameworks.Select(c => $"{c.Name} ({string.Join("; ", c.KeyClauses.Take(3))})"))}");
            if (!string.IsNullOrWhiteSpace(profile.SensitiveDataClassification))
                domainLines.Add($"Data classification: {profile.SensitiveDataClassification}");
            if (profile.SensitiveFieldPatterns.Count > 0)
                domainLines.Add($"Sensitive fields: {string.Join(", ", profile.SensitiveFieldPatterns.Take(20))}");
            if (profile.BusinessRules.Count > 0)
                domainLines.Add($"Business rules:\n{string.Join("\n", profile.BusinessRules.Take(15).Select(r => $"  - {r}"))}");
            if (profile.QualityAttributes.Count > 0)
                domainLines.Add($"Quality attributes:\n{string.Join("\n", profile.QualityAttributes.Take(10).Select(q => $"  - {q}"))}");
            if (profile.DomainGlossary.Count > 0)
                domainLines.Add($"Domain glossary:\n{string.Join("\n", profile.DomainGlossary.Take(20).Select(kv => $"  - {kv.Key}: {kv.Value}"))}");
            if (profile.DomainEvents.Count > 0)
                domainLines.Add($"Domain events: {string.Join(", ", profile.DomainEvents.Take(15).Select(e => e.Name))}");
            if (profile.IntegrationPatterns.Count > 0)
                domainLines.Add($"Integration patterns: {string.Join(", ", profile.IntegrationPatterns.Take(5).Select(p => p.Name))}");
            if (profile.Actors.Count > 0)
                domainLines.Add($"Actors: {string.Join(", ", profile.Actors.Take(10).Select(a => $"{a.Name} ({a.Role})"))}");

            if (domainLines.Count > 0)
                sections.Add($"## Domain Profile\n{string.Join("\n", domainLines)}");
        }

        // 4. Feedback from previous iterations
        var feedback = context.ReadFeedback(forAgent);
        if (feedback.Count > 0)
        {
            var selected = feedback.Take(maxFeedback).ToList();
            sections.Add($"## Feedback from Previous Iterations ({selected.Count}/{feedback.Count} items)\n{string.Join("\n", selected.Select(f => $"- {f}"))}");
        }

        // 5. Quality metrics from previous iterations
        var metrics = context.QualityMetrics.ToList();
        if (metrics.Count > 0)
        {
            var failing = metrics.Where(m => !m.Passed).Take(10).ToList();
            var summary = metrics
                .GroupBy(m => m.Category)
                .Select(g => $"  - {g.Key}: {g.Count(m => m.Passed)}/{g.Count()} passing")
                .ToList();

            var metricsBlock = $"Overall:\n{string.Join("\n", summary)}";
            if (failing.Count > 0)
                metricsBlock += $"\nFailing metrics:\n{string.Join("\n", failing.Select(m => $"  - [{m.Source}] {m.Category}: {m.Description} (value={m.Value}, target={m.Target})"))}";

            sections.Add($"## Quality Metrics\n{metricsBlock}");
        }

        // 6. Relevant agent results (cross-agent awareness)
        var results = context.AgentResults.ToList();
        if (results.Count > 0)
        {
            var relevant = results
                .Where(r => r.Value.Success && !string.IsNullOrWhiteSpace(r.Value.Summary))
                .Take(8)
                .Select(r => $"  - {r.Key}: {Truncate(r.Value.Summary, 150)}")
                .ToList();
            if (relevant.Count > 0)
                sections.Add($"## Prior Agent Results\n{string.Join("\n", relevant)}");
        }

        if (sections.Count == 0) return string.Empty;
        return string.Join("\n\n", sections);
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
