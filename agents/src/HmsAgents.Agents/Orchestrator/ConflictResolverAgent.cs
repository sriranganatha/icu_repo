using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Orchestrator;

/// <summary>
/// Detects and resolves conflicts when multiple agents produce overlapping artifacts.
/// </summary>
public sealed class ConflictResolverAgent : IAgent
{
    private readonly ILogger<ConflictResolverAgent> _logger;

    public AgentType Type => AgentType.ConflictResolver;
    public string Name => "Conflict Resolver";
    public string Description => "Detects and resolves artifact conflicts from parallel agent execution.";

    public ConflictResolverAgent(ILogger<ConflictResolverAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, "Scanning for artifact conflicts");

        var conflicts = DetectConflicts(context.Artifacts);
        foreach (var conflict in conflicts)
        {
            ResolveConflict(conflict, context);
            context.ArtifactConflicts.Add(conflict);
        }

        var resolved = conflicts.Count(c => c.Resolution != ConflictResolution.Unresolved);
        var manual = conflicts.Count - resolved;

        if (manual > 0)
        {
            context.Findings.Add(new ReviewFinding
            {
                Category = "CONFLICT-MANUAL",
                Severity = ReviewSeverity.Warning,
                Message = $"{manual} artifact conflict(s) require manual resolution: " + string.Join("; ", conflicts
                    .Where(c => c.Resolution == ConflictResolution.ManualRequired)
                    .Select(c => $"{c.FilePath}: {c.ProducerA} vs {c.ProducerB}")),
                Suggestion = "Review conflicting artifacts and choose the correct version."
            });
        }

        foreach (var item in context.CurrentClaimedItems)
            context.CompleteWorkItem?.Invoke(item);

        _logger.LogInformation("Conflict resolution: {Total} conflicts, {Resolved} auto-resolved, {Manual} manual",
            conflicts.Count, resolved, manual);

        context.AgentStatuses[Type] = AgentStatus.Completed;
        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Resolved {resolved}/{conflicts.Count} artifact conflicts ({manual} need manual review).",
            Duration = sw.Elapsed
        };
    }

    internal static List<ArtifactConflict> DetectConflicts(IEnumerable<CodeArtifact> artifacts)
    {
        var conflicts = new List<ArtifactConflict>();
        var byPath = artifacts
            .GroupBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in byPath)
        {
            var items = group.ToList();
            for (var i = 0; i < items.Count - 1; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    if (items[i].Content == items[j].Content) continue; // identical = no conflict
                    conflicts.Add(new ArtifactConflict
                    {
                        FilePath = group.Key,
                        ProducerA = items[i].ProducedBy.ToString(),
                        ProducerB = items[j].ProducedBy.ToString(),
                        ContentA = items[i].Content,
                        ContentB = items[j].Content
                    });
                }
            }
        }

        return conflicts;
    }

    private static void ResolveConflict(ArtifactConflict conflict, AgentContext context)
    {
        // Strategy: prefer the artifact from the more specialized (downstream) agent
        var priority = GetAgentPriority(conflict.ProducerA);
        var priorityB = GetAgentPriority(conflict.ProducerB);

        if (priority != priorityB)
        {
            if (priority > priorityB)
            {
                conflict.ResolvedContent = conflict.ContentA;
                conflict.Resolution = ConflictResolution.KeepFirst;
            }
            else
            {
                conflict.ResolvedContent = conflict.ContentB;
                conflict.Resolution = ConflictResolution.KeepSecond;
            }
        }
        else if (TryMerge(conflict))
        {
            conflict.Resolution = ConflictResolution.Merged;
        }
        else
        {
            conflict.Resolution = ConflictResolution.ManualRequired;
        }
    }

    private static bool TryMerge(ArtifactConflict conflict)
    {
        // Simple merge: if one is a superset of the other, pick the longer one
        if (conflict.ContentA.Contains(conflict.ContentB, StringComparison.Ordinal))
        {
            conflict.ResolvedContent = conflict.ContentA;
            return true;
        }
        if (conflict.ContentB.Contains(conflict.ContentA, StringComparison.Ordinal))
        {
            conflict.ResolvedContent = conflict.ContentB;
            return true;
        }
        return false;
    }

    private static int GetAgentPriority(string agentName) => agentName switch
    {
        nameof(AgentType.BugFix) => 100,
        nameof(AgentType.Review) => 90,
        nameof(AgentType.ServiceLayer) => 80,
        nameof(AgentType.Database) => 70,
        nameof(AgentType.Application) => 60,
        nameof(AgentType.Integration) => 50,
        nameof(AgentType.Testing) => 40,
        _ => 0
    };
}
