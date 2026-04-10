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
    private readonly ILogger<TraceabilityGateAgent> _logger;

    public AgentType Type => AgentType.TraceabilityGate;
    public string Name => "Traceability Gate";
    public string Description => "Builds requirement-to-artifact traceability matrix and blocks release on gaps.";

    public TraceabilityGateAgent(ILogger<TraceabilityGateAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, "Building traceability matrix");

        var matrix = BuildMatrix(context);
        var uncovered = matrix.Where(e => !e.FullyCovered).ToList();

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
