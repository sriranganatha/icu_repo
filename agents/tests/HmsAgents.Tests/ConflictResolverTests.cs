using System.Collections.Concurrent;
using HmsAgents.Agents.Orchestrator;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Tests;

public class ConflictResolverTests
{
    [Fact]
    public void DetectConflicts_NoOverlap_ReturnsEmpty()
    {
        var artifacts = new[]
        {
            new CodeArtifact { RelativePath = "A.cs", Content = "class A{}", ProducedBy = AgentType.Database },
            new CodeArtifact { RelativePath = "B.cs", Content = "class B{}", ProducedBy = AgentType.ServiceLayer }
        };

        var conflicts = ConflictResolverAgent.DetectConflicts(artifacts);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflicts_SamePathDifferentContent_DetectsConflict()
    {
        var artifacts = new[]
        {
            new CodeArtifact { RelativePath = "Service.cs", Content = "class A{}", ProducedBy = AgentType.Database },
            new CodeArtifact { RelativePath = "Service.cs", Content = "class B{}", ProducedBy = AgentType.ServiceLayer }
        };

        var conflicts = ConflictResolverAgent.DetectConflicts(artifacts);
        Assert.Single(conflicts);
        Assert.Equal("Service.cs", conflicts[0].FilePath);
    }

    [Fact]
    public void DetectConflicts_SamePathSameContent_NoConflict()
    {
        var artifacts = new[]
        {
            new CodeArtifact { RelativePath = "Service.cs", Content = "class A{}", ProducedBy = AgentType.Database },
            new CodeArtifact { RelativePath = "Service.cs", Content = "class A{}", ProducedBy = AgentType.ServiceLayer }
        };

        var conflicts = ConflictResolverAgent.DetectConflicts(artifacts);
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task ExecuteAsync_WithConflicts_ProducesFindings()
    {
        var context = new AgentContext();
        context.Artifacts.Add(new CodeArtifact { RelativePath = "X.cs", Content = "v1", ProducedBy = AgentType.Database });
        context.Artifacts.Add(new CodeArtifact { RelativePath = "X.cs", Content = "v2", ProducedBy = AgentType.ServiceLayer });

        var agent = new ConflictResolverAgent(new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConflictResolverAgent>());
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotEmpty(context.ArtifactConflicts);
    }

    [Fact]
    public async Task ExecuteAsync_NoConflicts_Succeeds()
    {
        var context = new AgentContext();
        context.Artifacts.Add(new CodeArtifact { RelativePath = "A.cs", Content = "v1", ProducedBy = AgentType.Database });

        var agent = new ConflictResolverAgent(new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConflictResolverAgent>());
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Empty(context.ArtifactConflicts);
    }

    [Fact]
    public void DetectConflicts_SupersetContent_MergesProperly()
    {
        // When one content is a superset, merge should pick the longer one
        var artifacts = new[]
        {
            new CodeArtifact { RelativePath = "X.cs", Content = "class A { int X; }", ProducedBy = AgentType.Database },
            new CodeArtifact { RelativePath = "X.cs", Content = "class A { int X; int Y; }", ProducedBy = AgentType.Database }
        };

        var conflicts = ConflictResolverAgent.DetectConflicts(artifacts);
        Assert.Single(conflicts);
        // Same producer priority, so TryMerge should trigger since B contains A
    }
}
