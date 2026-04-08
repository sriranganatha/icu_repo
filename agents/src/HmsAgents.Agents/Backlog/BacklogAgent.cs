using System.Diagnostics;
using System.Text.RegularExpressions;
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

        // 1. Backlog must consume only expander output.
        // Never synthesize backlog directly from raw requirements.
        if (context.ExpandedRequirements.Count == 0 && context.Requirements.Count > 0)
        {
            context.ReportProgress?.Invoke(Type,
                $"No expanded items available yet ({context.Requirements.Count} requirements). Waiting for RequirementsExpander output.");

            context.AgentStatuses[Type] = AgentStatus.Idle;
            return Task.FromResult(new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Backlog waiting: {context.Requirements.Count} requirements pending expansion",
                Duration = sw.Elapsed
            });
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

            if (hasArtifact && item.Status is WorkItemStatus.InQueue or WorkItemStatus.New)
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
                else if (children.Any(c => c.Status is WorkItemStatus.UnderDev or WorkItemStatus.Completed))
                {
                    if (item.Status is WorkItemStatus.New or WorkItemStatus.InQueue)
                    {
                        item.Status = WorkItemStatus.UnderDev;
                        item.StartedAt ??= DateTimeOffset.UtcNow;
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
        foreach (var item in context.ExpandedRequirements)
        {
            if (item.DependsOn.Count == 0) continue;

            if (item.Status == WorkItemStatus.InQueue || item.Status == WorkItemStatus.New)
            {
                // Check if any dependency is incomplete → block
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
            else if (item.Status == WorkItemStatus.Blocked)
            {
                // Re-evaluate: if ALL dependencies are now Completed → unblock
                var allDepsComplete = item.DependsOn.All(dep =>
                {
                    var depItem = context.ExpandedRequirements.FirstOrDefault(e => e.Id == dep);
                    return depItem is null || depItem.Status == WorkItemStatus.Completed;
                });

                if (allDepsComplete)
                {
                    item.Status = WorkItemStatus.InQueue;
                    _logger.LogInformation("[Backlog] Unblocked {ItemId} — all dependencies now complete", item.Id);
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

        foreach (var finding in context.Findings)
        {
            if (!ShouldCreateBugFromFinding(finding))
                continue;

            var sourceRef = $"FINDING:{finding.Id}";
            if (existingFindingRefs.Contains(sourceRef))
                continue;

            var bugId = $"BUG-AUTO-{finding.Id[..Math.Min(8, finding.Id.Length)].ToUpperInvariant()}";
            var title = BuildBugTitle(finding);
            var severity = MapBugSeverity(finding.Severity);
            var environment = BuildBugEnvironment(finding);
            var steps = BuildBugSteps(finding);
            var expected = BuildExpectedResult(finding);
            var actual = BuildActualResult(finding);

            context.ExpandedRequirements.Add(new ExpandedRequirement
            {
                Id = bugId,
                SourceRequirementId = sourceRef,
                ItemType = WorkItemType.Bug,
                Title = title,
                Severity = severity,
                Environment = environment,
                StepsToReproduce = steps,
                ExpectedResult = expected,
                ActualResult = actual,
                Description = $"Severity: {severity}\nEnvironment: {environment}\nSteps to Reproduce:\n{string.Join("\n", steps)}\nExpected Result: {expected}\nActual Result: {actual}\nAttachments: [Screenshot/Screen Recording/Log Snippet]",
                Module = InferModuleFromFinding(finding),
                Priority = severity is "Blocker" or "Critical" ? 1 : 2,
                Iteration = context.DevIteration,
                Tags = ["bugfix", finding.Category.ToLowerInvariant()],
                Status = WorkItemStatus.InQueue,
                ProducedBy = "Backlog",
            });

            existingFindingRefs.Add(sourceRef);
            added++;
        }

        return added;
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
        if (path.Contains("database", StringComparison.OrdinalIgnoreCase)) return "Database";
        if (path.Contains("service", StringComparison.OrdinalIgnoreCase)) return "ServiceLayer";
        if (path.Contains("api", StringComparison.OrdinalIgnoreCase) || path.Contains("web", StringComparison.OrdinalIgnoreCase)) return "Application";
        if (path.Contains("integration", StringComparison.OrdinalIgnoreCase)) return "Integration";
        return "General";
    }

    private record BacklogStats
    {
        public int Total, New, InQueue, UnderDev, Completed, Blocked;
    }
}
