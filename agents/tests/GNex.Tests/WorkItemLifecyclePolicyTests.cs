using GNex.Core.Models;
using FluentAssertions;

namespace GNex.Tests;

public class WorkItemLifecyclePolicyTests
{
    private static ExpandedRequirement Item(
        string id,
        WorkItemStatus status,
        int priority = 1,
        string[]? dependsOn = null,
        string[]? tags = null,
        DateTimeOffset? createdAt = null)
        => new()
        {
            Id = id,
            ItemType = WorkItemType.Task,
            Status = status,
            Title = id,
            Priority = priority,
            DependsOn = dependsOn?.ToList() ?? [],
            Tags = tags?.ToList() ?? ["service"],
            CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch
        };

    [Fact]
    public void Claim_OnlyClaimsFromQueue_NotFromNewPool()
    {
        var policy = new WorkItemLifecyclePolicy(_ => { })
        {
            BatchSize = 10,
            MaxQueueItems = 10,
            MaxInDevItems = 10
        };

        var items = new List<ExpandedRequirement>
        {
            Item("Q-1", WorkItemStatus.InQueue),
            Item("N-1", WorkItemStatus.New),
        };

        var claimed = policy.Claim(
            items,
            "ServiceLayer",
            _ => ["ServiceLayer"],
            _ => true);

        claimed.Select(c => c.Id).Should().BeEquivalentTo(["Q-1"]);
        items.Single(i => i.Id == "Q-1").Status.Should().Be(WorkItemStatus.Received);
        items.Single(i => i.Id == "N-1").Status.Should().Be(WorkItemStatus.New);
    }

    [Fact]
    public void Claim_RespectsDependencyReadiness()
    {
        var policy = new WorkItemLifecyclePolicy(_ => { })
        {
            BatchSize = 10,
            MaxQueueItems = 10,
            MaxInDevItems = 10
        };

        var items = new List<ExpandedRequirement>
        {
            Item("DEP", WorkItemStatus.New),
            Item("WAITING", WorkItemStatus.InQueue, dependsOn: ["DEP"]),
            Item("READY", WorkItemStatus.InQueue)
        };

        var claimed = policy.Claim(
            items,
            "ServiceLayer",
            _ => ["ServiceLayer"],
            _ => true);

        claimed.Select(c => c.Id).Should().BeEquivalentTo(["READY"]);
        items.Single(i => i.Id == "WAITING").Status.Should().Be(WorkItemStatus.InQueue);
    }

    [Fact]
    public void Claim_PrioritizesHighImpactFrontierThenPriorityThenCreatedAtThenId()
    {
        var policy = new WorkItemLifecyclePolicy(_ => { })
        {
            BatchSize = 2,
            MaxQueueItems = 10,
            MaxInDevItems = 10
        };

        var t0 = new DateTimeOffset(2026, 04, 08, 0, 0, 0, TimeSpan.Zero);
        var items = new List<ExpandedRequirement>
        {
            Item("A", WorkItemStatus.InQueue, priority: 2, createdAt: t0),
            Item("B", WorkItemStatus.InQueue, priority: 1, createdAt: t0),
            Item("C", WorkItemStatus.InQueue, priority: 1, createdAt: t0),
            Item("D", WorkItemStatus.New, dependsOn: ["A"]),
            Item("E", WorkItemStatus.New, dependsOn: ["A"]),
            Item("F", WorkItemStatus.New, dependsOn: ["B"]),
        };

        var claimed = policy.Claim(
            items,
            "ServiceLayer",
            _ => ["ServiceLayer"],
            _ => true);

        // A has highest downstream impact (2 dependents), then B beats C on priority.
        claimed.Select(c => c.Id).Should().ContainInOrder("A", "B");
    }

    [Fact]
    public void Claim_RespectsGlobalWipLimits()
    {
        var policy = new WorkItemLifecyclePolicy(_ => { })
        {
            BatchSize = 10,
            MaxQueueItems = 2,
            MaxInDevItems = 2
        };

        var items = new List<ExpandedRequirement>
        {
            Item("INPROG-1", WorkItemStatus.InProgress),
            Item("INPROG-2", WorkItemStatus.InProgress),
            Item("Q-1", WorkItemStatus.InQueue),
            Item("Q-2", WorkItemStatus.InQueue)
        };

        var claimed = policy.Claim(
            items,
            "ServiceLayer",
            _ => ["ServiceLayer"],
            _ => true);

        claimed.Should().BeEmpty();
        items.Count(i => i.Status == WorkItemStatus.Received).Should().Be(0);
    }
}
