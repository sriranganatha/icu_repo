using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Backlog;

/// <summary>
/// Tracks all work items across the pipeline: new requirements, in-progress development,
/// completed artifacts, and blocked items. Updates ExpandedRequirements statuses based on
/// actual artifact production and review findings.
/// </summary>
public sealed class BacklogAgent : IAgent
{
    private readonly ILogger<BacklogAgent> _logger;

    public AgentType Type => AgentType.Backlog;
    public string Name => "Backlog Manager";
    public string Description => "Tracks work items, manages backlog status, and coordinates iterative development.";

    public BacklogAgent(ILogger<BacklogAgent> logger) => _logger = logger;

    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("BacklogAgent starting — iteration {Iter}", context.DevIteration);

        // 1. Auto-generate backlog items from high-level requirements if none exist
        if (context.ExpandedRequirements.Count == 0 && context.Requirements.Count > 0)
        {
            context.ReportProgress?.Invoke(Type, $"No expanded items found — auto-generating backlog from {context.Requirements.Count} requirements");
            ExpandRequirementsToBacklog(context);
            context.ReportProgress?.Invoke(Type, $"Generated {context.ExpandedRequirements.Count} backlog items from requirements");
        }

        // 2. Update statuses based on current artifacts and findings
        context.ReportProgress?.Invoke(Type, $"Updating backlog statuses — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings");
        UpdateBacklogStatuses(context);

        // 3. Process inter-agent directives targeted at Backlog
        ProcessDirectives(context);

        // 4. Identify blocked items and send directives to unblock them
        IdentifyBlockedItems(context);

        var stats = GetBacklogStats(context);
        context.ReportProgress?.Invoke(Type, $"Backlog summary: {stats.Total} items — {stats.New} new, {stats.InQueue} queued, {stats.UnderDev} in-dev, {stats.Completed} done, {stats.Blocked} blocked");

        context.AgentStatuses[Type] = AgentStatus.Completed;

        return Task.FromResult(new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Backlog: {stats.Total} items — {stats.New} new, {stats.InQueue} queued, {stats.UnderDev} in-dev, {stats.Completed} done, {stats.Blocked} blocked",
            Duration = sw.Elapsed
        });
    }

    private void ExpandRequirementsToBacklog(AgentContext context)
    {
        var iteration = context.DevIteration;
        var seq = 0;

        foreach (var req in context.Requirements)
        {
            var module = !string.IsNullOrEmpty(req.Module) ? req.Module : "General";

            // Create an Epic for each top-level requirement
            var epicId = $"EPIC-{module}-{++seq:D3}";
            context.ExpandedRequirements.Add(new ExpandedRequirement
            {
                Id = epicId,
                SourceRequirementId = req.Id,
                ItemType = WorkItemType.Epic,
                Title = req.Title,
                Description = req.Description,
                Module = module,
                Priority = req.HeadingLevel <= 2 ? 1 : 2,
                Iteration = iteration,
                Tags = [.. req.Tags],
                AcceptanceCriteria = [.. req.AcceptanceCriteria],
                Status = WorkItemStatus.InQueue
            });

            // Create User Stories for each acceptance criterion
            var storySeq = 0;
            foreach (var ac in req.AcceptanceCriteria)
            {
                var storyId = $"US-{module}-{seq:D3}-{++storySeq:D2}";
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = storyId,
                    ParentId = epicId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.UserStory,
                    Title = $"As a user, {ac}",
                    Description = ac,
                    Module = module,
                    Priority = 2,
                    Iteration = iteration,
                    Status = WorkItemStatus.InQueue
                });

                // Create implementation Tasks under each story
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"TASK-{module}-{seq:D3}-{storySeq:D2}-DB",
                    ParentId = storyId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.Task,
                    Title = $"Database schema for: {Truncate(ac, 60)}",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["database"], Status = WorkItemStatus.InQueue
                });

                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"TASK-{module}-{seq:D3}-{storySeq:D2}-SVC",
                    ParentId = storyId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.Task,
                    Title = $"Service implementation for: {Truncate(ac, 60)}",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["service"], Status = WorkItemStatus.InQueue
                });

                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"TASK-{module}-{seq:D3}-{storySeq:D2}-TEST",
                    ParentId = storyId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.Task,
                    Title = $"Tests for: {Truncate(ac, 60)}",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["testing"], Status = WorkItemStatus.InQueue
                });
            }

            // If no acceptance criteria, create a UseCase
            if (req.AcceptanceCriteria.Count == 0)
            {
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"UC-{module}-{seq:D3}",
                    ParentId = epicId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.UseCase,
                    Title = $"Implement: {req.Title}",
                    Description = req.Description,
                    Module = module,
                    Priority = 2,
                    Iteration = iteration,
                    Status = WorkItemStatus.InQueue
                });
            }
        }

        _logger.LogInformation("Expanded {ReqCount} requirements → {BacklogCount} backlog items",
            context.Requirements.Count, context.ExpandedRequirements.Count);
    }

    private void UpdateBacklogStatuses(AgentContext context)
    {
        var artifactPaths = context.Artifacts.Select(a => a.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var artifactModules = context.Artifacts.Select(a => ExtractModule(a.Namespace)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in context.ExpandedRequirements)
        {
            if (item.Status == WorkItemStatus.Completed) continue;

            var hasArtifact = false;

            // Check by tag mapping
            if (item.Tags.Contains("database"))
                hasArtifact = context.Artifacts.Any(a => a.Layer == ArtifactLayer.Database && ModuleMatch(a.Namespace, item.Module));
            else if (item.Tags.Contains("service"))
                hasArtifact = context.Artifacts.Any(a => a.Layer == ArtifactLayer.Service && ModuleMatch(a.Namespace, item.Module));
            else if (item.Tags.Contains("testing"))
                hasArtifact = context.Artifacts.Any(a => a.Layer == ArtifactLayer.Test && ModuleMatch(a.Namespace, item.Module));
            else if (item.ItemType == WorkItemType.Epic || item.ItemType == WorkItemType.UserStory)
                hasArtifact = artifactModules.Contains(item.Module);

            if (hasArtifact && item.Status == WorkItemStatus.InQueue)
            {
                item.Status = WorkItemStatus.UnderDev;
                item.StartedAt = DateTimeOffset.UtcNow;
            }

            // Check if all child tasks are complete for parent items
            if (item.ItemType is WorkItemType.Epic or WorkItemType.UserStory)
            {
                var children = context.ExpandedRequirements.Where(c => c.ParentId == item.Id).ToList();
                if (children.Count > 0 && children.All(c => c.Status == WorkItemStatus.Completed))
                {
                    item.Status = WorkItemStatus.Completed;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                }
                else if (children.Any(c => c.Status == WorkItemStatus.UnderDev))
                {
                    item.Status = WorkItemStatus.UnderDev;
                    item.StartedAt ??= DateTimeOffset.UtcNow;
                }
            }

            // Mark tasks as completed if artifacts exist and no open findings for that module
            if (item.ItemType == WorkItemType.Task && item.Status == WorkItemStatus.UnderDev)
            {
                var hasOpenFindings = context.Findings.Any(f =>
                    f.Severity >= ReviewSeverity.Error &&
                    (f.FilePath?.Contains(item.Module, StringComparison.OrdinalIgnoreCase) ?? false));

                if (!hasOpenFindings && hasArtifact)
                {
                    item.Status = WorkItemStatus.Completed;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                }
            }
        }
    }

    private void ProcessDirectives(AgentContext context)
    {
        var processed = 0;
        while (context.DirectiveQueue.TryPeek(out var directive) && directive.To == AgentType.Backlog)
        {
            context.DirectiveQueue.TryDequeue(out _);
            processed++;

            switch (directive.Action)
            {
                case "ADD_WORK_ITEM":
                    context.ExpandedRequirements.Add(new ExpandedRequirement
                    {
                        Id = $"DIR-{Guid.NewGuid():N}"[..16],
                        ItemType = WorkItemType.Task,
                        Title = directive.Details,
                        Module = "General",
                        Priority = directive.Priority,
                        Iteration = context.DevIteration,
                        Status = WorkItemStatus.New
                    });
                    break;
                case "MARK_COMPLETED":
                    var item = context.ExpandedRequirements.FirstOrDefault(e => e.Id == directive.Details);
                    if (item is not null)
                    {
                        item.Status = WorkItemStatus.Completed;
                        item.CompletedAt = DateTimeOffset.UtcNow;
                    }
                    break;
            }
        }

        if (processed > 0)
            _logger.LogInformation("Processed {Count} directives", processed);
    }

    private void IdentifyBlockedItems(AgentContext context)
    {
        foreach (var item in context.ExpandedRequirements.Where(e => e.Status == WorkItemStatus.InQueue))
        {
            foreach (var dep in item.DependsOn)
            {
                var depItem = context.ExpandedRequirements.FirstOrDefault(e => e.Id == dep);
                if (depItem is not null && depItem.Status != WorkItemStatus.Completed)
                {
                    item.Status = WorkItemStatus.Blocked;
                    break;
                }
            }
        }
    }

    private static BacklogStats GetBacklogStats(AgentContext context) => new()
    {
        Total = context.ExpandedRequirements.Count,
        New = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.New),
        InQueue = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.InQueue),
        UnderDev = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.UnderDev),
        Completed = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Completed),
        Blocked = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Blocked),
    };

    private static bool ModuleMatch(string ns, string module) =>
        ns.Contains(module, StringComparison.OrdinalIgnoreCase) ||
        ns.Contains(module.Replace("Service", ""), StringComparison.OrdinalIgnoreCase);

    private static string ExtractModule(string ns) =>
        ns.Split('.').LastOrDefault() ?? "Unknown";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private record BacklogStats
    {
        public int Total, New, InQueue, UnderDev, Completed, Blocked;
    }
}
