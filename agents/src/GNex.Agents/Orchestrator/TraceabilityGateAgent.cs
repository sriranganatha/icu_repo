using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Orchestrator;

/// <summary>
/// Quality gate agent that builds a traceability matrix and blocks release if requirements are uncovered.
/// </summary>
public sealed class TraceabilityGateAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<TraceabilityGateAgent> _logger;

    public AgentType Type => AgentType.TraceabilityGate;
    public string Name => "Traceability Gate";
    public string Description => "Builds requirement-to-artifact traceability matrix and blocks release on gaps.";

    public TraceabilityGateAgent(ILlmProvider llm, ILogger<TraceabilityGateAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, "Building traceability matrix");

        var matrix = BuildMatrix(context);
        var uncovered = matrix.Where(e => !e.FullyCovered).ToList();

        // LLM-enhanced gap analysis for uncovered requirements
        if (uncovered.Count > 0)
            await AnalyzeGapsWithLlmAsync(uncovered, context, ct);

        // Produce traceability artifact
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "Docs/Quality/traceability-matrix.md",
            FileName = "traceability-matrix.md",
            Namespace = "GNex.Docs.Quality",
            ProducedBy = Type,
            Content = ExportMatrix(matrix),
            TracedRequirementIds = matrix.Select(e => e.RequirementId).ToList()
        });

        if (uncovered.Count > 0)
        {
            context.Findings.Add(new ReviewFinding
            {
                Category = "TRACE-GAP",
                Severity = ReviewSeverity.Warning,
                Message = $"{uncovered.Count} requirement(s) not fully covered: " + string.Join("; ", uncovered.Select(u =>
                    $"{u.RequirementId}: {u.ImplementingArtifacts.Count} artifact(s), {u.VerifyingTests.Count} test(s)")),
                Suggestion = "Ensure each requirement has at least one implementing artifact and one verifying test."
            });
        }

        foreach (var item in context.CurrentClaimedItems)
            context.CompleteWorkItem?.Invoke(item);

        _logger.LogInformation("Traceability: {Total} requirements, {Covered} covered, {Gaps} gaps",
            matrix.Count, matrix.Count - uncovered.Count, uncovered.Count);

        context.AgentStatuses[Type] = AgentStatus.Completed;
        return new AgentResult
        {
            Agent = Type,
            Success = uncovered.Count == 0,
            Summary = $"Traceability: {matrix.Count - uncovered.Count}/{matrix.Count} requirements fully covered.",
            Duration = sw.Elapsed
        };
    }

    private async Task AnalyzeGapsWithLlmAsync(List<TraceabilityEntry> uncovered, AgentContext context, CancellationToken ct)
    {
        var gapSummary = string.Join("\n", uncovered.Take(20).Select(u =>
            $"- {u.RequirementId}: {u.ImplementingArtifacts.Count} artifacts, {u.VerifyingTests.Count} tests"));

        var reqContext = string.Join("\n", uncovered.Take(20).Select(u =>
        {
            var req = context.Requirements.FirstOrDefault(r => r.Id == u.RequirementId);
            return req is not null ? $"- {req.Id}: {req.Title}" : $"- {u.RequirementId}: (title unknown)";
        }));

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a quality assurance engineer analyzing traceability gaps in a healthcare HMS system.
                For each gap, suggest what artifacts or tests are missing and why they matter.
                Be concise — one line per gap.
                """,
            UserPrompt = $"""
                These {uncovered.Count} requirements have traceability gaps:

                Requirements:
                {reqContext}

                Gap details:
                {gapSummary}

                For each, suggest: what artifact type is missing (DB migration, service, API endpoint, test) and the risk of not covering it.
                Format: REQ_ID|missing_artifact_type|risk_description
                """,
            Temperature = 0.2,
            MaxTokens = 1500,
            RequestingAgent = Name
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                foreach (var line in response.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = line.TrimStart('-', ' ').Split('|');
                    if (parts.Length >= 3)
                    {
                        context.Findings.Add(new ReviewFinding
                        {
                            Category = "TRACE-GAP-LLM",
                            Severity = ReviewSeverity.Warning,
                            Message = $"{parts[0].Trim()}: Missing {parts[1].Trim()} — {parts[2].Trim()}",
                            Suggestion = $"Create {parts[1].Trim()} for requirement {parts[0].Trim()}"
                        });
                    }
                }
                _logger.LogInformation("LLM gap analysis produced {Count} findings", 
                    context.Findings.Count(f => f.Category == "TRACE-GAP-LLM"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM gap analysis skipped");
        }
    }

    internal static List<TraceabilityEntry> BuildMatrix(AgentContext context)
    {
        var entries = new List<TraceabilityEntry>();
        var allReqIds = context.Requirements.Select(r => r.Id).ToHashSet();

        foreach (var reqId in allReqIds)
        {
            var artifacts = context.Artifacts
                .Where(a => a.TracedRequirementIds.Contains(reqId))
                .Select(a => a.RelativePath)
                .Distinct()
                .ToList();

            var tests = context.Artifacts
                .Where(a => a.Layer == ArtifactLayer.Test && a.TracedRequirementIds.Contains(reqId))
                .Select(a => a.RelativePath)
                .Distinct()
                .ToList();

            entries.Add(new TraceabilityEntry
            {
                RequirementId = reqId,
                ImplementingArtifacts = artifacts,
                VerifyingTests = tests
            });
        }

        return entries;
    }

    private static string ExportMatrix(List<TraceabilityEntry> matrix)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Traceability Matrix");
        sb.AppendLine();
        sb.AppendLine("| Requirement | Artifacts | Tests | Covered |");
        sb.AppendLine("|-------------|-----------|-------|---------|");

        foreach (var entry in matrix.OrderBy(e => e.RequirementId))
        {
            var covered = entry.FullyCovered ? "Yes" : "**NO**";
            sb.AppendLine($"| {entry.RequirementId} | {entry.ImplementingArtifacts.Count} | {entry.VerifyingTests.Count} | {covered} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**Coverage:** {matrix.Count(e => e.FullyCovered)}/{matrix.Count} requirements fully covered.");
        return sb.ToString();
    }
}
