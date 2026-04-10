using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Orchestrator;

/// <summary>
/// Sprint planner agent: estimates story points, bins items into sprints by capacity.
/// </summary>
public sealed class SprintPlannerAgent : IAgent
{
    private readonly ILogger<SprintPlannerAgent> _logger;

    public AgentType Type => AgentType.SprintPlanner;
    public string Name => "Sprint Planner";
    public string Description => "Estimates effort and allocates backlog items into sprints based on capacity.";

    public SprintPlannerAgent(ILogger<SprintPlannerAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        var items = context.ExpandedRequirements.ToList();
        if (items.Count == 0)
        {
            context.AgentStatuses[Type] = AgentStatus.Completed;
            return new AgentResult { Agent = Type, Success = true, Summary = "No items to plan.", Duration = sw.Elapsed };
        }

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Planning sprints for {items.Count} items");

        // Estimate points per item
        var estimated = items.Select(i => (Item: i, Points: EstimatePoints(i))).ToList();

        // Allocate into sprints (default 40 pts capacity)
        const int sprintCapacity = 40;
        var plans = AllocateSprints(estimated, sprintCapacity);
        context.SprintPlans.Clear();
        context.SprintPlans.AddRange(plans);

        // Produce artifact
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "Docs/Planning/sprint-plan.md",
            FileName = "sprint-plan.md",
            Namespace = "GNex.Docs.Planning",
            ProducedBy = Type,
            Content = ExportMarkdown(plans, estimated),
            TracedRequirementIds = items.Select(i => i.SourceRequirementId).Distinct().ToList()
        });

        foreach (var item in context.CurrentClaimedItems)
            context.CompleteWorkItem?.Invoke(item);

        _logger.LogInformation("Sprint planning: {Items} items → {Sprints} sprints",
            items.Count, plans.Count);

        context.AgentStatuses[Type] = AgentStatus.Completed;
        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Planned {items.Count} items across {plans.Count} sprint(s) ({plans.Sum(p => p.AllocatedPoints)} total points).",
            Duration = sw.Elapsed
        };
    }

    internal static int EstimatePoints(ExpandedRequirement item)
    {
        var points = 3; // base

        // Complexity from DOD count
        var dodCount = item.DefinitionOfDone?.Count ?? 0;
        if (dodCount > 5) points += 3;
        else if (dodCount > 2) points += 1;

        // Description length as complexity proxy
        var descLen = item.Description?.Length ?? 0;
        if (descLen > 500) points += 3;
        else if (descLen > 200) points += 1;

        // Tags hinting at complexity
        var tags = string.Join(" ", item.Tags ?? []).ToLowerInvariant();
        if (tags.Contains("integration") || tags.Contains("fhir") || tags.Contains("hl7")) points += 2;
        if (tags.Contains("security") || tags.Contains("hipaa")) points += 2;
        if (tags.Contains("migration") || tags.Contains("database")) points += 1;

        return Math.Min(points, 13); // Cap at 13 (Fibonacci ceiling)
    }

    internal static List<SprintPlan> AllocateSprints(
        List<(ExpandedRequirement Item, int Points)> estimated, int capacity)
    {
        var plans = new List<SprintPlan>();
        var sprintNum = 1;
        var current = new SprintPlan { SprintNumber = sprintNum, CapacityPoints = capacity };

        // Sort by priority: highest first
        var sorted = estimated.OrderByDescending(e => e.Item.Priority).ToList();

        foreach (var (item, pts) in sorted)
        {
            if (current.AllocatedPoints + pts > capacity && current.ItemIds.Count > 0)
            {
                plans.Add(current);
                sprintNum++;
                current = new SprintPlan { SprintNumber = sprintNum, CapacityPoints = capacity };
            }
            current.ItemIds.Add(item.Id);
            current.AllocatedPoints += pts;
        }

        if (current.ItemIds.Count > 0)
            plans.Add(current);

        return plans;
    }

    private static string ExportMarkdown(List<SprintPlan> plans, List<(ExpandedRequirement Item, int Points)> estimated)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Sprint Plan");
        sb.AppendLine();
        sb.AppendLine($"**Total Items:** {estimated.Count}  ");
        sb.AppendLine($"**Total Points:** {estimated.Sum(e => e.Points)}  ");
        sb.AppendLine($"**Sprints:** {plans.Count}");
        sb.AppendLine();

        var lookup = estimated.ToDictionary(e => e.Item.Id);

        foreach (var plan in plans)
        {
            sb.AppendLine($"## Sprint {plan.SprintNumber}");
            sb.AppendLine();
            sb.AppendLine($"**Capacity:** {plan.CapacityPoints} pts | **Allocated:** {plan.AllocatedPoints} pts | **Utilization:** {plan.UtilizationPercent:F0}%");
            sb.AppendLine();
            sb.AppendLine("| Item | Points | Priority |");
            sb.AppendLine("|------|--------|----------|");

            foreach (var id in plan.ItemIds)
            {
                if (lookup.TryGetValue(id, out var entry))
                {
                    var title = entry.Item.Title?.Length > 50 ? entry.Item.Title[..50] + "…" : entry.Item.Title ?? id;
                    sb.AppendLine($"| {title} | {entry.Points} | {entry.Item.Priority} |");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
