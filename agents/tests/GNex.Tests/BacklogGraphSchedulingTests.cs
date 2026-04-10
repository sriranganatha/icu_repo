using GNex.Agents.Backlog;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests;

public class BacklogGraphSchedulingTests
{
    private static ExpandedRequirement Task(
        string id,
        WorkItemStatus status = WorkItemStatus.New,
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

    private static AgentContext ContextWith(params ExpandedRequirement[] items)
        => new()
        {
            PipelineConfig = new PipelineConfig
            {
                RequirementsPath = Path.GetTempPath(),
                OutputPath = Path.GetTempPath(),
                MaxQueueItems = 2,
                MaxInDevItems = 10,
                SpinUpDocker = false,
                ExecuteDdl = false
            },
            ExpandedRequirements = new SynchronizedList<ExpandedRequirement>(items)
        };

    [Fact]
    public async Task ExecuteAsync_DependencyWaitingItemsStayInPool_NotBlocked()
    {
        var logger = new Mock<ILogger<BacklogAgent>>();
        var agent = new BacklogAgent(new Mock<ILlmProvider>().Object, logger.Object);

        var ctx = ContextWith(
            Task("DEP", WorkItemStatus.New),
            Task("CHILD", WorkItemStatus.InQueue, dependsOn: ["DEP"])
        );

        await agent.ExecuteAsync(ctx);

        ctx.ExpandedRequirements.Single(i => i.Id == "CHILD").Status.Should().Be(WorkItemStatus.New);
        ctx.ExpandedRequirements.Count(i => i.Status == WorkItemStatus.Blocked).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_AdmitsQueueFromDependencyFrontier_ByGraphImpact()
    {
        var logger = new Mock<ILogger<BacklogAgent>>();
        var agent = new BacklogAgent(new Mock<ILlmProvider>().Object, logger.Object);
        var t0 = new DateTimeOffset(2026, 04, 08, 0, 0, 0, TimeSpan.Zero);

        var ctx = ContextWith(
            Task("A", priority: 2, createdAt: t0),
            Task("B", priority: 1, createdAt: t0),
            Task("C", priority: 1, createdAt: t0),
            Task("D", dependsOn: ["A"]),
            Task("E", dependsOn: ["A"]),
            Task("F", dependsOn: ["B"])
        );

        await agent.ExecuteAsync(ctx);

        var queued = ctx.ExpandedRequirements
            .Where(i => i.Status == WorkItemStatus.InQueue)
            .Select(i => i.Id)
            .ToList();

        // Queue cap is 2. A has highest downstream impact, then B over C by priority.
        queued.Should().ContainInOrder("A", "B");
        queued.Should().HaveCount(2);
        ctx.ExpandedRequirements.Single(i => i.Id == "C").Status.Should().Be(WorkItemStatus.New);
    }

    [Fact]
    public async Task ExecuteAsync_DeterministicTieBreaker_UsesItemId()
    {
        var logger = new Mock<ILogger<BacklogAgent>>();
        var agent = new BacklogAgent(new Mock<ILlmProvider>().Object, logger.Object);
        var t0 = new DateTimeOffset(2026, 04, 08, 0, 0, 0, TimeSpan.Zero);

        var ctx = ContextWith(
            Task("A", priority: 1, createdAt: t0),
            Task("B", priority: 1, createdAt: t0),
            Task("C", priority: 1, createdAt: t0)
        );

        await agent.ExecuteAsync(ctx);
        var firstQueue = ctx.ExpandedRequirements.Where(i => i.Status == WorkItemStatus.InQueue).Select(i => i.Id).OrderBy(x => x).ToList();

        await agent.ExecuteAsync(ctx);
        var secondQueue = ctx.ExpandedRequirements.Where(i => i.Status == WorkItemStatus.InQueue).Select(i => i.Id).OrderBy(x => x).ToList();

        firstQueue.Should().Equal(secondQueue);
        firstQueue.Should().Equal(["A", "B"]);
    }
}
