namespace GNex.Core.Models;

/// <summary>
/// Enforces a consistent work-item lifecycle for every agent:
///   New/InQueue → Received → InProgress → Completed
///                                       → Failed (retry up to MaxRetries, then back to New as backlog)
///
/// Usage from the orchestrator:
///   var batch = policy.Claim(context, agentType);   // New/InQueue → Received
///   policy.Start(batch);                             // Received → InProgress
///   // … agent.ExecuteAsync …
///   if (success) policy.Complete(context, agentType);
///   else         policy.Fail(context, agentType);    // InProgress → Failed → retry or backlog
/// </summary>
public sealed class WorkItemLifecyclePolicy
{
    /// <summary>Max retries per work-item before it is returned to the backlog as New.</summary>
    public int MaxItemRetries { get; init; } = 3;

    /// <summary>Max items an agent may claim in a single cycle.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Global cap on items in Received state (queued, waiting to start).</summary>
    public int MaxQueueItems { get; set; } = 50;

    /// <summary>Global cap on items in InProgress state (actively being worked on).</summary>
    public int MaxInDevItems { get; set; } = 50;

    private readonly Action<string> _log;

    public WorkItemLifecyclePolicy(Action<string> log) => _log = log;

    // ────────────────────────────────────────────────────────────
    //  Stage 1: CLAIM   New/InQueue → Received
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Claim up to <see cref="BatchSize"/> matching items for <paramref name="agentName"/>.
    /// Transitions each item from New/InQueue → Received and sets AssignedAgent.
    /// Returns the list of items claimed.
    /// </summary>
    public List<ExpandedRequirement> Claim(
        IList<ExpandedRequirement> allItems,
        string agentName,
        Func<ExpandedRequirement, IReadOnlyList<string>> getRelevantAgents,
        Func<ExpandedRequirement, bool> matchesSingleAgent)
    {
        var claimed = new List<ExpandedRequirement>();

        // Enforce global WIP limits: count items already in Received (queue) and InProgress (in-dev)
        var currentReceived = 0;
        var currentInProgress = 0;
        foreach (var it in allItems)
        {
            if (!IsActionable(it))
                continue;

            if (it.Status == WorkItemStatus.Received) currentReceived++;
            else if (it.Status == WorkItemStatus.InProgress) currentInProgress++;
        }

        var queueSlots = Math.Max(0, MaxQueueItems - currentReceived);
        var devSlots = Math.Max(0, MaxInDevItems - currentInProgress);
        // The effective limit is the smallest of batch size, available queue slots, and available dev slots
        var effectiveLimit = Math.Min(BatchSize, Math.Min(queueSlots, devSlots));

        if (effectiveLimit <= 0)
        {
            _log($"[Lifecycle] {agentName} skipped claiming — WIP limits reached (queue={currentReceived}/{MaxQueueItems}, inDev={currentInProgress}/{MaxInDevItems})");
            return claimed;
        }

        var byId = allItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var downstreamDependents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in allItems)
        {
            foreach (var depId in candidate.DependsOn)
            {
                if (!byId.ContainsKey(depId))
                    continue;

                downstreamDependents.TryGetValue(depId, out var count);
                downstreamDependents[depId] = count + 1;
            }
        }

        var orderedCandidates = allItems
            // Strict lifecycle: only queued items are claimable.
            // Items stay in New (pool) until admitted to queue.
            .Where(item => item.Status == WorkItemStatus.InQueue)
            .Where(item =>
            {
                if (item.DependsOn.Count == 0)
                    return true;

                foreach (var depId in item.DependsOn)
                {
                    if (!byId.TryGetValue(depId, out var dep))
                        continue;
                    if (dep.ItemType is WorkItemType.Epic or WorkItemType.UserStory)
                        continue;
                    if (dep.Status != WorkItemStatus.Completed)
                        return false;
                }

                return true;
            })
            .OrderByDescending(item => downstreamDependents.TryGetValue(item.Id, out var c) ? c : 0)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.CreatedAt)
            .ToList();

        foreach (var item in orderedCandidates)
        {
            if (claimed.Count >= effectiveLimit) break;

            if (item.ItemType == WorkItemType.Task)
            {
                var relevantAgents = getRelevantAgents(item);
                if (!relevantAgents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
                    continue;

                item.AssignedAgent = string.Join(",", relevantAgents);
                item.Status = WorkItemStatus.Received;
                item.StartedAt ??= DateTimeOffset.UtcNow;
                claimed.Add(item);
                continue;
            }

            if (!string.IsNullOrEmpty(item.AssignedAgent)) continue;

            if (matchesSingleAgent(item))
            {
                item.AssignedAgent = agentName;
                item.Status = WorkItemStatus.Received;
                item.StartedAt ??= DateTimeOffset.UtcNow;
                claimed.Add(item);
            }
        }

        if (claimed.Count > 0)
            _log($"[Lifecycle] {agentName} claimed {claimed.Count} items (Received)");

        return claimed;
    }

    // ────────────────────────────────────────────────────────────
    //  Stage 2: START   Received → InProgress
    // ────────────────────────────────────────────────────────────

    /// <summary>Transition all claimed items from Received → InProgress just before the agent starts work.</summary>
    public void Start(IEnumerable<ExpandedRequirement> claimedItems, string agentName)
    {
        var count = 0;
        foreach (var item in claimedItems)
        {
            if (item.Status != WorkItemStatus.Received) continue;
            if (string.IsNullOrWhiteSpace(item.AssignedAgent))
                item.AssignedAgent = agentName;
            item.Status = WorkItemStatus.InProgress;
            count++;
        }

        if (count > 0)
            _log($"[Lifecycle] {agentName} started work on {count} items (InProgress)");
    }

    // ────────────────────────────────────────────────────────────
    //  Stage 3a: COMPLETE   InProgress → Completed
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Mark items as Completed for <paramref name="agentName"/>.
    /// For multi-agent tasks, tracks per-agent completion and only marks the item
    /// Completed when ALL relevant agents have completed.
    /// </summary>
    public int Complete(
        IList<ExpandedRequirement> allItems,
        string agentName,
        Func<ExpandedRequirement, IReadOnlyList<string>> getRelevantAgents,
        ConcurrentTaskTracker taskTracker)
    {
        var completedCount = 0;

        foreach (var item in allItems)
        {
            if (item.Status == WorkItemStatus.Completed) continue;

            if (item.ItemType == WorkItemType.Task)
            {
                var relevantAgents = getRelevantAgents(item);
                if (!relevantAgents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
                    continue;

                // Atomic mark + check: only one agent gets true, preventing double-complete
                if (taskTracker.TryComplete(item.Id, agentName, relevantAgents))
                {
                    item.Status = WorkItemStatus.Completed;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                    item.AssignedAgent = string.Join(",", relevantAgents);
                    completedCount++;
                }
                continue;
            }

            if (item.AssignedAgent != agentName) continue;

            item.Status = WorkItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            completedCount++;
        }

        // Propagate: if all children of a parent are Completed → complete the parent
        PropagateParentCompletion(allItems);

        if (completedCount > 0)
            _log($"[Lifecycle] {agentName} completed {completedCount} items");

        return completedCount;
    }

    // ────────────────────────────────────────────────────────────
    //  Stage 3a-single: COMPLETE one item   InProgress → Completed
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Complete a single work item for <paramref name="agentName"/>.
    /// For multi-agent tasks, uses <paramref name="taskTracker"/> and only
    /// marks Completed when ALL relevant agents have finished.
    /// Called by agents themselves after processing each item.
    /// </summary>
    public bool CompleteItem(
        ExpandedRequirement item,
        string agentName,
        IList<ExpandedRequirement> allItems,
        Func<ExpandedRequirement, IReadOnlyList<string>> getRelevantAgents,
        ConcurrentTaskTracker taskTracker)
    {
        if (item.Status == WorkItemStatus.Completed) return false;

        if (item.ItemType == WorkItemType.Task)
        {
            var relevantAgents = getRelevantAgents(item);
            if (!relevantAgents.Contains(agentName, StringComparer.OrdinalIgnoreCase))
                return false;

            if (taskTracker.TryComplete(item.Id, agentName, relevantAgents))
            {
                item.Status = WorkItemStatus.Completed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                item.AssignedAgent = string.Join(",", relevantAgents);
                PropagateParentCompletion(allItems);
                _log($"[Lifecycle] {agentName} completed item {item.Id}: {item.Title}");
                return true;
            }
            return false;
        }

        if (item.AssignedAgent != agentName) return false;

        item.Status = WorkItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        PropagateParentCompletion(allItems);
        _log($"[Lifecycle] {agentName} completed item {item.Id}: {item.Title}");
        return true;
    }

    // ────────────────────────────────────────────────────────────
    //  Stage 3b-single: FAIL one item   InProgress → retry / backlog
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fail a single work item. Retries if under <see cref="MaxItemRetries"/>,
    /// otherwise returns the item to backlog (New).
    /// Called by agents when they cannot process a specific item.
    /// </summary>
    public void FailItem(
        ExpandedRequirement item,
        string agentName,
        string reason,
        ConcurrentTaskTracker? taskTracker = null)
    {
        if (item.Status is not (WorkItemStatus.InProgress or WorkItemStatus.Received))
            return;

        item.RetryCount++;
        item.LastFailedAgent = agentName;
        taskTracker?.RemoveAgent(item.Id, agentName);

        if (item.RetryCount < MaxItemRetries)
        {
            item.Status = WorkItemStatus.InQueue;
            item.AssignedAgent = string.Empty;
            _log($"[Lifecycle] {agentName} failed item {item.Id} — retry {item.RetryCount}/{MaxItemRetries}: {reason}");
        }
        else
        {
            item.Status = WorkItemStatus.New;
            item.AssignedAgent = string.Empty;
            item.StartedAt = null;
            _log($"[Lifecycle] {agentName} failed item {item.Id} — backlogged after {MaxItemRetries} retries: {reason}");
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Stage 3b: FAIL   InProgress → Failed → retry or backlog
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Handle failure for all InProgress/Received items assigned to <paramref name="agentName"/>.
    /// If item.RetryCount &lt; MaxItemRetries → increment retry, set Failed (will be re-claimed next cycle).
    /// If retries exhausted → return to New (backlog) so a different agent run can attempt it.
    /// </summary>
    public (int retriable, int backlogged) Fail(
        IList<ExpandedRequirement> allItems,
        string agentName,
        ConcurrentTaskTracker? taskTracker = null)
    {
        int retriable = 0, backlogged = 0;

        foreach (var item in allItems)
        {
            if (item.Status is not (WorkItemStatus.InProgress or WorkItemStatus.Received)) continue;
            if (string.IsNullOrEmpty(item.AssignedAgent)) continue;

            var assignedAgents = item.AssignedAgent.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!assignedAgents.Contains(agentName, StringComparer.OrdinalIgnoreCase)) continue;

            item.RetryCount++;
            item.LastFailedAgent = agentName;

            // Clear this agent from the task tracker so multi-agent tasks
            // don't deadlock waiting for a failed agent to complete
            taskTracker?.RemoveAgent(item.Id, agentName);

            if (item.RetryCount < MaxItemRetries)
            {
                // Will be re-claimed on the next cycle — set back to InQueue
                item.Status = WorkItemStatus.InQueue;
                item.AssignedAgent = string.Empty; // clear so GetClaimableItems can re-claim
                retriable++;
            }
            else
            {
                // Exhausted retries — return to backlog
                item.Status = WorkItemStatus.New;
                item.AssignedAgent = string.Empty;
                item.StartedAt = null;
                backlogged++;
            }
        }

        if (retriable > 0)
            _log($"[Lifecycle] {agentName} failed — {retriable} items queued for retry (max {MaxItemRetries})");
        if (backlogged > 0)
            _log($"[Lifecycle] {agentName} failed — {backlogged} items returned to backlog after {MaxItemRetries} retries");

        return (retriable, backlogged);
    }

    // ────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────

    private static void PropagateParentCompletion(IList<ExpandedRequirement> allItems)
    {
        var byParent = allItems
            .Where(c => !string.IsNullOrWhiteSpace(c.ParentId))
            .GroupBy(c => c.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var parent in allItems.Where(e => e.ItemType is WorkItemType.Epic or WorkItemType.UserStory))
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

            parent.Status = WorkItemStatus.InProgress;
            parent.CompletedAt = null;
            parent.AssignedAgent = string.Empty;
        }
    }

    private static bool IsActionable(ExpandedRequirement item) =>
        item.ItemType is WorkItemType.Task or WorkItemType.Bug or WorkItemType.UseCase;
}

/// <summary>
/// Thread-safe tracker for multi-agent task completion.
/// Tracks which agents have completed work on which task items.
/// Uses lock-per-item to make MarkDone+AllDone atomic and avoid TOCTOU races.
/// </summary>
public sealed class ConcurrentTaskTracker
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ItemState> _items = new(StringComparer.OrdinalIgnoreCase);

    public void MarkDone(string itemId, string agentName)
    {
        var state = _items.GetOrAdd(itemId, _ => new ItemState());
        lock (state.Lock)
        {
            state.CompletedAgents[agentName] = 1;
        }
    }

    public bool AllDone(string itemId, IReadOnlyList<string> requiredAgents)
    {
        if (!_items.TryGetValue(itemId, out var state)) return false;
        lock (state.Lock)
        {
            return requiredAgents.All(a => state.CompletedAgents.ContainsKey(a));
        }
    }

    /// <summary>
    /// Atomically marks an agent done and returns true only if ALL required agents
    /// are now complete. Prevents the TOCTOU race where two agents both see "all done".
    /// </summary>
    public bool TryComplete(string itemId, string agentName, IReadOnlyList<string> requiredAgents)
    {
        var state = _items.GetOrAdd(itemId, _ => new ItemState());
        lock (state.Lock)
        {
            if (state.Completed) return false; // already finalized
            state.CompletedAgents[agentName] = 1;
            if (requiredAgents.All(a => state.CompletedAgents.ContainsKey(a)))
            {
                state.Completed = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Clear all tracked state so a pipeline reset starts completely fresh.
    /// </summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// When an agent fails a task, remove it from tracking so the task can be retried
    /// by a fresh cycle without a deadlock waiting for the failed agent.
    /// </summary>
    public void RemoveAgent(string itemId, string agentName)
    {
        if (_items.TryGetValue(itemId, out var state))
        {
            lock (state.Lock)
            {
                state.CompletedAgents.TryRemove(agentName, out _);
                state.Completed = false; // allow re-completion after retry
            }
        }
    }

    private sealed class ItemState
    {
        public readonly object Lock = new();
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> CompletedAgents = new(StringComparer.OrdinalIgnoreCase);
        public bool Completed;
    }
}
