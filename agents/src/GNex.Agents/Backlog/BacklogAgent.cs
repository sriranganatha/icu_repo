using System.Diagnostics;
using System.Text.RegularExpressions;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Backlog;

/// <summary>
/// Tracks all work items across the pipeline: new requirements, in-progress development,
/// completed artifacts, and blocked items. Updates ExpandedRequirements statuses based on
/// actual artifact production and review findings.
/// </summary>
public sealed class BacklogAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<BacklogAgent> _logger;
    private const int MaxAutoBugsPerRun = 100;

    public AgentType Type => AgentType.Backlog;
    public string Name => "Backlog Manager";
    public string Description => "Tracks work items, manages backlog status, and coordinates iterative development.";

    public BacklogAgent(ILlmProvider llm, ILogger<BacklogAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("BacklogAgent starting — iteration {Iter}", context.DevIteration);

        try
        {
            // 1. Backlog must consume only expander output.
            // Never synthesize backlog directly from raw requirements.
            if (context.ExpandedRequirements.Count == 0 && context.Requirements.Count > 0)
            {
                context.ReportProgress?.Invoke(Type,
                    $"No expanded items available yet ({context.Requirements.Count} requirements). Waiting for RequirementsExpander output.");

                context.AgentStatuses[Type] = AgentStatus.Idle;
                return new AgentResult
                {
                    Agent = Type,
                    Success = true,
                    Summary = $"Backlog waiting: {context.Requirements.Count} requirements pending expansion",
                    Duration = sw.Elapsed
                };
            }

            // 1b. Convert cross-agent findings into standardized bug backlog items
            var bugItemsAdded = EnsureBugItemsFromFindings(context);
            if (bugItemsAdded > 0)
                context.ReportProgress?.Invoke(Type, $"Added {bugItemsAdded} bug item(s) from findings using bug report template");

            // 2. Update statuses based on current artifacts and findings
            context.ReportProgress?.Invoke(Type, $"Updating backlog statuses — {context.Artifacts.Count} artifacts, {context.Findings.Count} findings");
            UpdateBacklogStatuses(context);

            // 3. Process inter-agent directives targeted at Backlog
            ProcessDirectives(context);

            // 4. Identify blocked items and send directives to unblock them
            IdentifyBlockedItems(context);

            // 5. LLM-based prioritization for new items
            await PrioritizeWithLlmAsync(context, ct);

            var stats = GetBacklogStats(context);
            context.ReportProgress?.Invoke(Type,
                $"Backlog summary: {stats.Total} items — {stats.New} new, {stats.ReadyBacklog} ready, {stats.ActiveQueue} active-queue, {stats.InDev} in-dev, {stats.Completed} done, {stats.Blocked} blocked");

            // Only mark Backlog as Completed when every actionable item is done.
            // This keeps the orchestrator's backlog-driven re-dispatch loop active.
            var actionable = context.ExpandedRequirements.Where(IsActionableWorkItem).ToList();
            var allDone = actionable.Count > 0 && actionable.All(i => i.Status == WorkItemStatus.Completed);
            context.AgentStatuses[Type] = allDone ? AgentStatus.Completed : AgentStatus.Idle;

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = allDone
                    ? $"Backlog COMPLETE: all {actionable.Count} actionable items done"
                    : $"Backlog: {stats.Total} items — {stats.New} new, {stats.ReadyBacklog} ready, {stats.ActiveQueue} active-queue, {stats.InDev} in-dev, {stats.Completed} done, {stats.Blocked} blocked",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BacklogAgent failed — {ExType}: {Message}", ex.GetType().Name, ex.Message);
            context.AgentStatuses[Type] = AgentStatus.Failed;
            return new AgentResult
            {
                Agent = Type, Success = false,
                Errors = [ex.ToString()],
                Summary = $"Backlog failed: {ex.GetType().Name}: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }

    private async Task PrioritizeWithLlmAsync(AgentContext context, CancellationToken ct)
    {
        var newItems = context.ExpandedRequirements
            .Where(i => i.Status is WorkItemStatus.New or WorkItemStatus.InQueue && i.ItemType == WorkItemType.UserStory)
            .Take(30)
            .ToList();
        if (newItems.Count == 0) return;

        var itemSummary = string.Join("\n", newItems.Select(i => $"- {i.Id}|{i.Title}|{i.Module}|P{i.Priority}|{i.StoryPoints}pts"));

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a healthcare project backlog prioritizer. Given a list of backlog items,
                re-rank them by business value and technical dependency order.
                Output ONLY the item IDs in priority order (highest first), one per line. No explanations.
                """,
            UserPrompt = $"""
                Prioritize these {newItems.Count} backlog items for a Hospital Management System:
                {itemSummary}

                Consider: patient safety > compliance > core workflows > enhancements.
                Output IDs only, one per line, highest priority first.
                """,
            Temperature = 0.2,
            MaxTokens = 1000,
            RequestingAgent = Name
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var rankedIds = response.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => line.Trim('-', ' ', '*'))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                var priority = 1;
                foreach (var id in rankedIds)
                {
                    var item = newItems.FirstOrDefault(i => i.Id == id);
                    if (item is not null)
                        item.Priority = priority++;
                }
                _logger.LogInformation("LLM re-prioritized {Count} backlog items", rankedIds.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM prioritization skipped — using existing priorities");
        }
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
                var storyTitle = BuildUserStoryTitle(ac, req.Title);
                var storyCriteria = NormalizeAcceptanceCriteria(ac);
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = storyId,
                    ParentId = epicId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.UserStory,
                    Title = storyTitle,
                    Description = ac,
                    Module = module,
                    Priority = 2,
                    Iteration = iteration,
                    StoryPoints = 3,
                    Labels = ["API", "Backend"],
                    AcceptanceCriteria = storyCriteria,
                    Status = WorkItemStatus.InQueue
                });

                // Create implementation Tasks under each story
                var taskSubject = BuildTaskSubject(ac, req.Title);
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"TASK-{module}-{seq:D3}-{storySeq:D2}-DB",
                    ParentId = storyId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.Task,
                    Title = $"[T-{seq:D3}-{storySeq:D2}-DB] Create persistence layer for {taskSubject}",
                    Description = $"Implement database-side changes to support: {taskSubject}.",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["database"],
                    TechnicalNotes = "Use PostgreSQL best practices, tenant-safe constraints, and indexed lookup fields.",
                    DefinitionOfDone = ["[ ] Unit tests passed.", "[ ] Documentation updated in Swagger.", "[ ] Code reviewed by peer."],
                    Status = WorkItemStatus.InQueue
                });

                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"TASK-{module}-{seq:D3}-{storySeq:D2}-SVC",
                    ParentId = storyId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.Task,
                    Title = $"[T-{seq:D3}-{storySeq:D2}-SVC] Implement service/API logic for {taskSubject}",
                    Description = $"Implement backend behavior and endpoints required for: {taskSubject}.",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["service", "api"],
                    TechnicalNotes = "Implement validation, error handling, and auditable state transitions.",
                    DefinitionOfDone = ["[ ] Unit tests passed.", "[ ] Documentation updated in Swagger.", "[ ] Code reviewed by peer."],
                    Status = WorkItemStatus.InQueue
                });

                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"TASK-{module}-{seq:D3}-{storySeq:D2}-TEST",
                    ParentId = storyId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.Task,
                    Title = $"[T-{seq:D3}-{storySeq:D2}-TEST] Add automated tests for {taskSubject}",
                    Description = $"Add test coverage and negative-path checks for: {taskSubject}.",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["testing"],
                    TechnicalNotes = "Cover happy path, edge cases, authorization failures, and validation errors.",
                    DefinitionOfDone = ["[ ] Unit tests passed.", "[ ] Documentation updated in Swagger.", "[ ] Code reviewed by peer."],
                    Status = WorkItemStatus.InQueue
                });
            }

            // If no acceptance criteria, create a UseCase
            if (req.AcceptanceCriteria.Count == 0)
            {
                var ucTitle = BuildUseCaseTitle(req.Title);
                var ucMainFlow = BuildUseCaseMainFlow(req.Title);
                context.ExpandedRequirements.Add(new ExpandedRequirement
                {
                    Id = $"UC-{module}-{seq:D3}",
                    ParentId = epicId,
                    SourceRequirementId = req.Id,
                    ItemType = WorkItemType.UseCase,
                    Title = ucTitle,
                    Actor = "Registered User",
                    Preconditions = "User is on the relevant entry page and has network connectivity.",
                    MainFlow = ucMainFlow,
                    Postconditions = "Requested action is completed, persisted, and auditable.",
                    Description = $"Actor: Registered User\nPreconditions: User is on the relevant entry page and has network connectivity.\nMain Flow: {string.Join("; ", ucMainFlow)}\nPostconditions: Requested action is completed, persisted, and auditable.",
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

    private static string BuildUserStoryTitle(string ac, string requirementTitle)
    {
        var action = ac.Trim();
        if (action.StartsWith("As a ", StringComparison.OrdinalIgnoreCase))
            return action;

        return $"As a clinical user, I want to {ToSentenceFragment(action)} so that {ToSentenceFragment(requirementTitle)} is delivered safely and efficiently.";
    }

    private static List<string> NormalizeAcceptanceCriteria(string ac)
    {
        var trimmed = ac.Trim();
        if (trimmed.Contains("given", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Contains("when", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Contains("then", StringComparison.OrdinalIgnoreCase))
        {
            return [trimmed];
        }

        return [$"Given the user is authorized, when they {ToSentenceFragment(trimmed)}, then the system completes the request and records an auditable outcome."];
    }

    private static string ToSentenceFragment(string value)
    {
        var v = value.Trim().Trim('.', ';', ':', ',');
        if (string.IsNullOrWhiteSpace(v)) return "complete the requested operation";
        return char.ToLowerInvariant(v[0]) + v[1..];
    }

    private static string BuildUseCaseTitle(string requirementTitle)
    {
        var normalized = requirementTitle.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "Complete Requested Workflow";

        return normalized.StartsWith("Reset ", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"Execute {normalized}";
    }

    private static List<string> BuildUseCaseMainFlow(string requirementTitle)
    {
        var action = ToSentenceFragment(requirementTitle);
        return
        [
            "1. User initiates the workflow from the UI.",
            "2. System prompts for required input.",
            $"3. User submits valid input to {action}.",
            "4. System validates and processes the request securely.",
            "5. System confirms success to the user."
        ];
    }

    private void UpdateBacklogStatuses(AgentContext context)
    {
        var artifactModules = context.Artifacts.Select(a => ExtractModule(a.Namespace)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pre-build per-layer module sets to avoid scanning all artifacts per requirement (O(n*m) → O(n+m))
        var dbModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var svcModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var testModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in context.Artifacts)
        {
            var mod = a.Namespace; // full namespace for ModuleMatch
            switch (a.Layer)
            {
                case ArtifactLayer.Database: dbModules.Add(mod); break;
                case ArtifactLayer.Service: svcModules.Add(mod); break;
                case ArtifactLayer.Test: testModules.Add(mod); break;
            }
        }

        // Read WIP limits from pipeline config
        var maxInDev = context.PipelineConfig?.MaxInDevItems ?? 50;

        // Cache InDev count — recount only when we promote an item
        var currentInDev = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.UnderDev);

        foreach (var item in context.ExpandedRequirements)
        {
            if (item.Status == WorkItemStatus.Completed) continue;

            var hasArtifact = false;

            // Check by tag mapping using pre-built per-layer sets
            if (item.Tags.Contains("database"))
                hasArtifact = dbModules.Any(ns => ModuleMatch(ns, item.Module));
            else if (item.Tags.Contains("service"))
                hasArtifact = svcModules.Any(ns => ModuleMatch(ns, item.Module));
            else if (item.Tags.Contains("testing") || item.Tags.Contains("e2e"))
                hasArtifact = testModules.Any(ns => ModuleMatch(ns, item.Module));
            else if (item.Tags.Contains("api") || item.Tags.Contains("contract"))
                hasArtifact = svcModules.Any(ns => ModuleMatch(ns, item.Module))
                           || artifactModules.Contains(item.Module);
            else if (item.Tags.Contains("bugfix") || item.ItemType == WorkItemType.Bug)
                hasArtifact = true; // Bugs target existing code — always promotable
            else if (item.ItemType == WorkItemType.Epic || item.ItemType == WorkItemType.UserStory)
                hasArtifact = artifactModules.Contains(item.Module);

            if (hasArtifact && item.Status is WorkItemStatus.InQueue or WorkItemStatus.New)
            {
                if (currentInDev < maxInDev)
                {
                    item.Status = WorkItemStatus.UnderDev;
                    item.StartedAt = DateTimeOffset.UtcNow;
                    currentInDev++;
                }
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
                else if (children.Any(c => c.Status is WorkItemStatus.UnderDev or WorkItemStatus.Completed))
                {
                    if (item.Status is WorkItemStatus.New or WorkItemStatus.InQueue)
                    {
                        if (currentInDev < maxInDev)
                        {
                            item.Status = WorkItemStatus.UnderDev;
                            item.StartedAt ??= DateTimeOffset.UtcNow;
                            currentInDev++;
                        }
                    }
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

            // Mark bugs as completed when findings for their module are resolved
            if (item.ItemType == WorkItemType.Bug && item.Status == WorkItemStatus.UnderDev)
            {
                var hasOpenFindings = context.Findings.Any(f =>
                    f.Severity >= ReviewSeverity.Error &&
                    (f.FilePath?.Contains(item.Module, StringComparison.OrdinalIgnoreCase) ?? false));

                if (!hasOpenFindings)
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
                        Title = directive.Details.StartsWith("[T-", StringComparison.OrdinalIgnoreCase)
                            ? directive.Details
                            : "[T-DIR-001] " + directive.Details,
                        Description = directive.Details,
                        TechnicalNotes = "Follow service standards, add validation, and keep auditable logs.",
                        DefinitionOfDone = ["[ ] Unit tests passed.", "[ ] Documentation updated in Swagger.", "[ ] Code reviewed by peer."],
                        Module = "General",
                        Priority = directive.Priority,
                        Iteration = context.DevIteration,
                        Tags = ["service"],
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
        RollupParentItems(context.ExpandedRequirements);

        var maxQueue = context.PipelineConfig?.MaxQueueItems ?? 50;
        var maxInDev = context.PipelineConfig?.MaxInDevItems ?? 50;
        // Promote enough items to fill both queue + in-dev slots; WIP limits enforced in Claim().
        var promotionCap = (maxQueue + maxInDev) * 2;
        var items = context.ExpandedRequirements
            .Where(IsActionableWorkItem)
            .Where(i => i.Status is not (WorkItemStatus.Completed or WorkItemStatus.Received or WorkItemStatus.InProgress))
            // Skip items that have already been claimed (have an AssignedAgent) to avoid
            // a race condition where Claim() sets AssignedAgent + Received concurrently
            // and this method resets the item back to InQueue.
            .Where(i => string.IsNullOrEmpty(i.AssignedAgent))
            .ToList();

        var byId = context.ExpandedRequirements
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var downstreamDependents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in context.ExpandedRequirements)
        {
            foreach (var depId in item.DependsOn)
            {
                if (!byId.ContainsKey(depId)) continue;
                downstreamDependents.TryGetValue(depId, out var count);
                downstreamDependents[depId] = count + 1;
            }
        }

        var ready = items
            .Where(item => item.DependsOn.Count == 0 || item.DependsOn.All(depId =>
            {
                if (!byId.TryGetValue(depId, out var depItem)) return true;
                if (!IsActionableWorkItem(depItem)) return true;
                if (depItem.Status == WorkItemStatus.Completed) return true;
                return IsDepAgentFailed(depItem, context);
            }))
            .OrderByDescending(item => downstreamDependents.TryGetValue(item.Id, out var c) ? c : 0)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Id)
            .Take(maxQueue)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (ready.Contains(item.Id))
            {
                // Promote to InQueue if not already there
                if (item.Status != WorkItemStatus.InQueue)
                    item.Status = WorkItemStatus.InQueue;
            }
            else
            {
                // Item is not in the ready frontier — demote InQueue items back to New
                // so that dependency-blocked or over-cap items don't stay queued.
                if (item.Status == WorkItemStatus.InQueue)
                    item.Status = WorkItemStatus.New;
            }
        }
    }

    private static bool IsActionableWorkItem(ExpandedRequirement item) =>
        item.ItemType is WorkItemType.Task or WorkItemType.Bug;

    private static void RollupParentItems(IList<ExpandedRequirement> allItems)
    {
        var byParent = allItems
            .Where(c => !string.IsNullOrWhiteSpace(c.ParentId))
            .GroupBy(c => c.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var parent in allItems.Where(e => e.ItemType is WorkItemType.Epic or WorkItemType.UserStory or WorkItemType.UseCase))
        {
            if (!byParent.TryGetValue(parent.Id, out var children) || children.Count == 0)
                continue;

            if (children.All(c => c.Status == WorkItemStatus.Completed))
            {
                parent.Status = WorkItemStatus.Completed;
                parent.CompletedAt ??= DateTimeOffset.UtcNow;
                parent.AssignedAgent = string.Empty;
                continue;
            }

            // Only promote to InProgress if not already Completed (avoid oscillation)
            if (parent.Status != WorkItemStatus.Completed)
            {
                if (children.Any(c => c.Status is WorkItemStatus.UnderDev or WorkItemStatus.InProgress or WorkItemStatus.Completed))
                    parent.Status = WorkItemStatus.InProgress;
            }
            parent.AssignedAgent = string.Empty;
        }
    }

    /// <summary>Check if the agent that owns a dependency item has permanently failed.</summary>
    private static bool IsDepAgentFailed(ExpandedRequirement depItem, AgentContext context)
    {
        foreach (var tag in depItem.Tags)
        {
            if (string.Equals(tag, "database", StringComparison.OrdinalIgnoreCase) &&
                context.AgentStatuses.TryGetValue(AgentType.Database, out var dbStatus) && dbStatus == AgentStatus.Failed)
                return true;
            if (string.Equals(tag, "service", StringComparison.OrdinalIgnoreCase) &&
                context.AgentStatuses.TryGetValue(AgentType.ServiceLayer, out var svcStatus) && svcStatus == AgentStatus.Failed)
                return true;
            if (string.Equals(tag, "testing", StringComparison.OrdinalIgnoreCase) &&
                context.AgentStatuses.TryGetValue(AgentType.Testing, out var testStatus) && testStatus == AgentStatus.Failed)
                return true;
            if ((string.Equals(tag, "api", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "application", StringComparison.OrdinalIgnoreCase)) &&
                context.AgentStatuses.TryGetValue(AgentType.Application, out var appStatus) && appStatus == AgentStatus.Failed)
                return true;
            if (string.Equals(tag, "integration", StringComparison.OrdinalIgnoreCase) &&
                context.AgentStatuses.TryGetValue(AgentType.Integration, out var intStatus) && intStatus == AgentStatus.Failed)
                return true;
        }
        return false;
    }

    private static BacklogStats GetBacklogStats(AgentContext context) => new()
    {
        Total = context.ExpandedRequirements.Count,
        New = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.New),
        ReadyBacklog = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.InQueue),
        ActiveQueue = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Received),
        InDev = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.InProgress && IsActionableWorkItem(e)),
        Completed = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Completed),
        Blocked = context.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Blocked),
    };

    private static bool ModuleMatch(string ns, string module) =>
        !string.IsNullOrWhiteSpace(module) &&
        !string.Equals(module, "General", StringComparison.OrdinalIgnoreCase) &&
        (ns.Contains(module, StringComparison.OrdinalIgnoreCase) ||
         ns.Contains(module.Replace("Service", ""), StringComparison.OrdinalIgnoreCase));

    private static string ExtractModule(string ns) =>
        ns.Split('.').LastOrDefault() ?? "Unknown";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static string BuildTaskSubject(string acceptanceCriterion, string fallbackTitle)
    {
        var ac = acceptanceCriterion?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ac))
            return Truncate(fallbackTitle, 48);

        // Remove common GWT prefixes so task titles remain technical and concise.
        ac = Regex.Replace(ac, @"\bGiven\b.*?\bwhen\b", string.Empty, RegexOptions.IgnoreCase).Trim();
        ac = Regex.Replace(ac, @"\bthen\b", string.Empty, RegexOptions.IgnoreCase).Trim();
        ac = ac.Trim(',', '.', ';', ':');

        if (string.IsNullOrWhiteSpace(ac))
            ac = fallbackTitle;

        return Truncate(ac, 48);
    }

    private static int EnsureBugItemsFromFindings(AgentContext context)
    {
        var added = 0;
        var existingFindingRefs = context.ExpandedRequirements
            .Where(e => e.ItemType == WorkItemType.Bug && !string.IsNullOrWhiteSpace(e.SourceRequirementId))
            .Select(e => e.SourceRequirementId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingFingerprints = context.ExpandedRequirements
            .Where(e => e.ItemType == WorkItemType.Bug)
            .Select(BuildBugFingerprint)
            .Where(fp => !string.IsNullOrWhiteSpace(fp))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in context.Findings)
        {
            if (added >= MaxAutoBugsPerRun)
                break;

            if (!ShouldCreateBugFromFinding(finding))
                continue;

            var sourceRef = $"FINDING:{finding.Id}";
            if (existingFindingRefs.Contains(sourceRef))
                continue;

            var fingerprint = BuildFindingFingerprint(finding);
            if (!string.IsNullOrWhiteSpace(fingerprint) && existingFingerprints.Contains(fingerprint))
                continue;

            var bugId = $"BUG-AUTO-{finding.Id[..Math.Min(8, finding.Id.Length)].ToUpperInvariant()}";
            var title = BuildBugTitle(finding);
            var severity = MapBugSeverity(finding.Severity);
            var environment = BuildBugEnvironment(finding);
            var steps = BuildBugSteps(finding);
            var expected = BuildExpectedResult(finding);
            var actual = BuildActualResult(finding);

            var bugModule = InferModuleFromFinding(finding);
            var svcDef = InferServiceDefFromFinding(finding);
            var svcLabel = svcDef?.Name ?? "UnknownService";
            var schema = svcDef?.Schema ?? "unknown";
            var entityCsv = svcDef is not null ? string.Join(", ", svcDef.Entities) : "";
            var svcNs = svcDef?.Namespace ?? $"GNex.{bugModule}";

            context.ExpandedRequirements.Add(new ExpandedRequirement
            {
                Id = bugId,
                SourceRequirementId = sourceRef,
                ItemType = WorkItemType.Bug,
                Title = title,
                Severity = severity,
                Environment = svcDef is not null
                    ? $".NET 10, PostgreSQL 16 | Service: {svcLabel}, Schema: {schema}, Entities: {entityCsv} — {environment}"
                    : environment,
                StepsToReproduce = steps,
                ExpectedResult = expected,
                ActualResult = actual,
                Description = svcDef is not null
                    ? $"[{svcLabel} | {schema}] Severity: {severity}\nEnvironment: .NET 10, PostgreSQL 16, Service: {svcLabel}, Schema: {schema}\nSteps to Reproduce:\n{string.Join("\n", steps)}\nExpected Result: {expected}\nActual Result: {actual}"
                    : $"Severity: {severity}\nEnvironment: {environment}\nSteps to Reproduce:\n{string.Join("\n", steps)}\nExpected Result: {expected}\nActual Result: {actual}\nAttachments: [Screenshot/Screen Recording/Log Snippet]",
                TechnicalNotes = svcDef is not null
                    ? $"Investigate in {svcNs}. Check entities: {entityCsv}. Schema: {schema}."
                    : null,
                Module = svcDef is not null ? svcDef.Name.Replace("Service", "") : bugModule,
                AffectedServices = svcDef is not null ? [svcLabel] : [],
                Priority = severity is "Blocker" or "Critical" ? 1 : 2,
                Iteration = context.DevIteration,
                Tags = ["bugfix", (finding.Category ?? "general").ToLowerInvariant()],
                // New status lets lifecycle policy enforce queue/in-dev limits consistently.
                Status = WorkItemStatus.New,
                ProducedBy = "Backlog",
            });

            existingFindingRefs.Add(sourceRef);
            if (!string.IsNullOrWhiteSpace(fingerprint))
                existingFingerprints.Add(fingerprint);
            added++;
        }

        return added;
    }

    private static string BuildFindingFingerprint(ReviewFinding finding)
    {
        var category = (finding.Category ?? string.Empty).Trim().ToLowerInvariant();
        var file = (finding.FilePath ?? string.Empty).Trim().ToLowerInvariant();
        var message = Regex.Replace((finding.Message ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        return string.Join("|", [category, file, message]);
    }

    private static string BuildBugFingerprint(ExpandedRequirement bug)
    {
        var title = Regex.Replace((bug.Title ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        var module = (bug.Module ?? string.Empty).Trim().ToLowerInvariant();
        var severity = (bug.Severity ?? string.Empty).Trim().ToLowerInvariant();
        return string.Join("|", [title, module, severity]);
    }

    private static bool ShouldCreateBugFromFinding(ReviewFinding finding)
        => finding.Severity is ReviewSeverity.Error
            or ReviewSeverity.Critical
            or ReviewSeverity.SecurityViolation
            or ReviewSeverity.ComplianceViolation;

    private static string BuildBugTitle(ReviewFinding finding)
    {
        var msg = string.IsNullOrWhiteSpace(finding.Message) ? "Unhandled failure detected" : finding.Message.Trim();
        return $"[BUG] {Truncate(msg, 90)}";
    }

    private static string MapBugSeverity(ReviewSeverity severity) => severity switch
    {
        ReviewSeverity.Critical => "Critical",
        ReviewSeverity.SecurityViolation => "Critical",
        ReviewSeverity.ComplianceViolation => "Critical",
        ReviewSeverity.Error => "Major",
        ReviewSeverity.Warning => "Minor",
        _ => "Minor"
    };

    private static string BuildBugEnvironment(ReviewFinding finding)
        => string.IsNullOrWhiteSpace(finding.FilePath)
            ? ".NET 8, PostgreSQL 16, Local"
            : $".NET 8, PostgreSQL 16, Local — {finding.FilePath}";

    private static List<string> BuildBugSteps(ReviewFinding finding)
        =>
        [
            "1. Navigate to the impacted workflow or endpoint.",
            "2. Perform the action under normal expected input.",
            $"3. Observe failure: {Truncate(finding.Message, 120)}"
        ];

    private static string BuildExpectedResult(ReviewFinding finding)
        => string.IsNullOrWhiteSpace(finding.Suggestion)
            ? "The operation should complete with expected validation and stable UI/API behavior."
            : finding.Suggestion;

    private static string BuildActualResult(ReviewFinding finding)
        => string.IsNullOrWhiteSpace(finding.Message)
            ? "The operation fails or produces unstable behavior."
            : finding.Message;

    private static string InferModuleFromFinding(ReviewFinding finding)
    {
        var path = finding.FilePath ?? string.Empty;
        // Try to match a known microservice from the file path
        var svc = InferServiceDefFromFinding(finding);
        if (svc is not null) return svc.Name.Replace("Service", "");
        if (path.Contains("database", StringComparison.OrdinalIgnoreCase)) return "Database";
        if (path.Contains("service", StringComparison.OrdinalIgnoreCase)) return "ServiceLayer";
        if (path.Contains("api", StringComparison.OrdinalIgnoreCase) || path.Contains("web", StringComparison.OrdinalIgnoreCase)) return "Application";
        if (path.Contains("integration", StringComparison.OrdinalIgnoreCase)) return "Integration";
        return "General";
    }

    private static MicroserviceDefinition? InferServiceDefFromFinding(ReviewFinding finding)
    {
        var combined = $"{finding.FilePath ?? ""} {finding.Message ?? ""} {finding.Category ?? ""}".ToLowerInvariant();
        foreach (var svc in MicroserviceCatalog.All)
        {
            var svcLower = svc.Name.Replace("Service", "").ToLowerInvariant();
            if (combined.Contains(svcLower) ||
                combined.Contains(svc.ShortName) ||
                svc.Entities.Any(e => combined.Contains(e.ToLowerInvariant())))
                return svc;
        }
        return null;
    }

    private record BacklogStats
    {
        public int Total, New, ReadyBacklog, ActiveQueue, InDev, Completed, Blocked;
    }
}
