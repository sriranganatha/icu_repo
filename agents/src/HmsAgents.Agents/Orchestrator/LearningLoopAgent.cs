using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

/// <summary>
/// Learning loop agent: records agent performance metrics and surfaces improvement recommendations.
/// </summary>
public sealed class LearningLoopAgent : IAgent
{
    private readonly ILogger<LearningLoopAgent> _logger;

    public AgentType Type => AgentType.LearningLoop;
    public string Name => "Learning Loop";
    public string Description => "Analyzes pipeline run metrics and produces optimization recommendations.";

    public LearningLoopAgent(ILogger<LearningLoopAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, "Analyzing pipeline performance");

        var records = context.LearningRecords.ToList();
        var recommendations = Analyze(records, context);

        // Publish recommendations
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "Docs/Quality/learning-report.md",
            FileName = "learning-report.md",
            Namespace = "Hms.Docs.Quality",
            ProducedBy = Type,
            Content = ExportReport(records, recommendations, context.RunId)
        });

        foreach (var item in context.CurrentClaimedItems)
            context.CompleteWorkItem?.Invoke(item);

        _logger.LogInformation("Learning loop: {Records} records, {Recs} recommendations",
            records.Count, recommendations.Count);

        context.AgentStatuses[Type] = AgentStatus.Completed;
        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Analyzed {records.Count} agent runs, produced {recommendations.Count} recommendations.",
            Duration = sw.Elapsed
        };
    }

    /// <summary>
    /// Record an agent run result into the learning record bag.
    /// Call from the orchestrator after each agent completes.
    /// </summary>
    public static void RecordRun(AgentContext context, AgentType agent, AgentResult result, string taskType = "pipeline")
    {
        context.LearningRecords.Add(new AgentLearningRecord
        {
            RunId = context.RunId,
            AgentType = agent.ToString(),
            TaskType = taskType,
            Succeeded = result.Success,
            Duration = result.Duration,
            RetryCount = context.RetryAttempts.GetValueOrDefault(agent, 0),
            ArtifactsProduced = context.Artifacts.Count(a => a.ProducedBy == agent)
        });
    }

    internal static List<string> Analyze(List<AgentLearningRecord> records, AgentContext context)
    {
        var recs = new List<string>();

        if (records.Count == 0)
        {
            recs.Add("No agent run records available — ensure RecordRun is called after each agent.");
            return recs;
        }

        // Slowest agents
        var slowest = records
            .Where(r => r.Succeeded)
            .OrderByDescending(r => r.Duration)
            .Take(3)
            .ToList();
        foreach (var s in slowest)
        {
            if (s.Duration > TimeSpan.FromMinutes(2))
                recs.Add($"PERF: {s.AgentType} took {s.Duration.TotalSeconds:F0}s — consider caching or splitting.");
        }

        // High retry agents
        var highRetry = records
            .Where(r => r.RetryCount > 1)
            .GroupBy(r => r.AgentType)
            .Select(g => (Agent: g.Key, AvgRetries: g.Average(r => r.RetryCount)))
            .Where(x => x.AvgRetries > 1.5)
            .ToList();
        foreach (var (agent, avg) in highRetry)
            recs.Add($"RELIABILITY: {agent} averages {avg:F1} retries — investigate root cause.");

        // Agents that never produced artifacts
        var noArtifacts = records
            .Where(r => r.Succeeded && r.ArtifactsProduced == 0)
            .Select(r => r.AgentType)
            .Distinct()
            .ToList();
        foreach (var agent in noArtifacts)
            recs.Add($"OUTPUT: {agent} succeeded but produced no artifacts — may not be generating expected output.");

        // Failure rate per agent
        var failureRate = records
            .GroupBy(r => r.AgentType)
            .Select(g => (Agent: g.Key, Rate: g.Count(r => !r.Succeeded) / (double)g.Count()))
            .Where(x => x.Rate > 0.3)
            .ToList();
        foreach (var (agent, rate) in failureRate)
            recs.Add($"FAILURE: {agent} has {rate:P0} failure rate — needs investigation.");

        // Overall pipeline health
        var totalDuration = context.CompletedAt.HasValue
            ? context.CompletedAt.Value - context.StartedAt
            : DateTimeOffset.UtcNow - context.StartedAt;
        if (totalDuration > TimeSpan.FromMinutes(30))
            recs.Add($"PIPELINE: Total run time {totalDuration.TotalMinutes:F0}min exceeds 30min target.");

        return recs;
    }

    private static string ExportReport(List<AgentLearningRecord> records, List<string> recommendations, string runId)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Pipeline Learning Report");
        sb.AppendLine();
        sb.AppendLine($"**Run ID:** `{runId}`  ");
        sb.AppendLine($"**Agent Runs Analyzed:** {records.Count}");
        sb.AppendLine();

        // Agent summary table
        sb.AppendLine("## Agent Performance");
        sb.AppendLine();
        sb.AppendLine("| Agent | Succeeded | Duration | Retries | Artifacts |");
        sb.AppendLine("|-------|-----------|----------|---------|-----------|");

        foreach (var r in records.OrderBy(r => r.AgentType.ToString()))
        {
            sb.AppendLine($"| {r.AgentType} | {(r.Succeeded ? "Yes" : "No")} | {r.Duration.TotalSeconds:F1}s | {r.RetryCount} | {r.ArtifactsProduced} |");
        }
        sb.AppendLine();

        // Recommendations
        sb.AppendLine("## Recommendations");
        sb.AppendLine();
        if (recommendations.Count > 0)
        {
            foreach (var rec in recommendations)
                sb.AppendLine($"- {rec}");
        }
        else
        {
            sb.AppendLine("No issues detected — pipeline is performing within expected parameters.");
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
