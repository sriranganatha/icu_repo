using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

public class SprintPlannerTests
{
    [Fact]
    public void EstimatePoints_SimpleItem_ReturnsBasePoints()
    {
        var item = new ExpandedRequirement { Title = "Simple task", Description = "Short", Tags = [] };
        var pts = SprintPlannerAgent.EstimatePoints(item);
        Assert.InRange(pts, 3, 13);
    }

    [Fact]
    public void EstimatePoints_ComplexItem_HigherPoints()
    {
        var item = new ExpandedRequirement
        {
            Title = "Complex integration",
            Description = new string('x', 600),
            Tags = ["integration", "fhir", "security"],
            DefinitionOfDone = ["A", "B", "C", "D", "E", "F"]
        };
        var pts = SprintPlannerAgent.EstimatePoints(item);
        Assert.True(pts > 3);
        Assert.True(pts <= 13);
    }

    [Fact]
    public void EstimatePoints_CapsAt13()
    {
        var item = new ExpandedRequirement
        {
            Description = new string('x', 1000),
            Tags = ["integration", "fhir", "security", "hipaa", "migration", "database"],
            DefinitionOfDone = Enumerable.Range(0, 10).Select(i => $"DOD-{i}").ToList()
        };
        var pts = SprintPlannerAgent.EstimatePoints(item);
        Assert.Equal(13, pts);
    }

    [Fact]
    public void AllocateSprints_FitsInOneSprint()
    {
        var items = new List<(ExpandedRequirement, int)>
        {
            (new ExpandedRequirement { Id = "1", Priority = 1 }, 5),
            (new ExpandedRequirement { Id = "2", Priority = 2 }, 5)
        };

        var plans = SprintPlannerAgent.AllocateSprints(items, 40);

        Assert.Single(plans);
        Assert.Equal(10, plans[0].AllocatedPoints);
        Assert.Equal(2, plans[0].ItemIds.Count);
    }

    [Fact]
    public void AllocateSprints_SpillsToMultipleSprints()
    {
        var items = new List<(ExpandedRequirement, int)>
        {
            (new ExpandedRequirement { Id = "1", Priority = 3 }, 25),
            (new ExpandedRequirement { Id = "2", Priority = 2 }, 20),
            (new ExpandedRequirement { Id = "3", Priority = 1 }, 15)
        };

        var plans = SprintPlannerAgent.AllocateSprints(items, 40);

        Assert.True(plans.Count >= 2);
        Assert.Equal(plans.Sum(p => p.ItemIds.Count), 3);
    }

    [Fact]
    public void AllocateSprints_EmptyInput_ReturnsEmpty()
    {
        var plans = SprintPlannerAgent.AllocateSprints([], 40);
        Assert.Empty(plans);
    }

    [Fact]
    public void AllocateSprints_HighPriorityFirst()
    {
        var items = new List<(ExpandedRequirement, int)>
        {
            (new ExpandedRequirement { Id = "low", Priority = 1 }, 5),
            (new ExpandedRequirement { Id = "high", Priority = 10 }, 5)
        };

        var plans = SprintPlannerAgent.AllocateSprints(items, 40);

        Assert.Single(plans);
        // High priority sorted first
        Assert.Equal("high", plans[0].ItemIds[0]);
    }

    [Fact]
    public async Task ExecuteAsync_ProducesSprintPlanArtifact()
    {
        var context = new AgentContext();
        context.ExpandedRequirements.Add(new ExpandedRequirement { Id = "1", Title = "Task 1", Priority = 5, SourceRequirementId = "R1" });
        context.ExpandedRequirements.Add(new ExpandedRequirement { Id = "2", Title = "Task 2", Priority = 3, SourceRequirementId = "R1" });

        var agent = new SprintPlannerAgent(new Microsoft.Extensions.Logging.Abstractions.NullLogger<SprintPlannerAgent>());
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotEmpty(context.SprintPlans);
        Assert.Contains(context.Artifacts, a => a.RelativePath.Contains("sprint-plan"));
    }
}
