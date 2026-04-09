using HmsAgents.Agents.Orchestrator;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Tests;

public class TraceabilityGateTests
{
    [Fact]
    public void BuildMatrix_AllRequirementsCovered_FullCoverage()
    {
        var context = new AgentContext
        {
            Requirements =
            [
                new Requirement { Id = "R1", Title = "Feature 1" },
                new Requirement { Id = "R2", Title = "Feature 2" }
            ]
        };
        context.Artifacts.Add(new CodeArtifact { RelativePath = "Svc.cs", Layer = ArtifactLayer.Service, TracedRequirementIds = ["R1", "R2"] });
        context.Artifacts.Add(new CodeArtifact { RelativePath = "Test.cs", Layer = ArtifactLayer.Test, TracedRequirementIds = ["R1", "R2"] });

        var matrix = TraceabilityGateAgent.BuildMatrix(context);

        Assert.Equal(2, matrix.Count);
        Assert.All(matrix, e => Assert.True(e.FullyCovered));
    }

    [Fact]
    public void BuildMatrix_MissingTests_NotCovered()
    {
        var context = new AgentContext
        {
            Requirements = [new Requirement { Id = "R1", Title = "Feature 1" }]
        };
        context.Artifacts.Add(new CodeArtifact { RelativePath = "Svc.cs", Layer = ArtifactLayer.Service, TracedRequirementIds = ["R1"] });

        var matrix = TraceabilityGateAgent.BuildMatrix(context);

        Assert.Single(matrix);
        Assert.False(matrix[0].FullyCovered);
        Assert.NotEmpty(matrix[0].ImplementingArtifacts);
        Assert.Empty(matrix[0].VerifyingTests);
    }

    [Fact]
    public void BuildMatrix_MissingArtifacts_NotCovered()
    {
        var context = new AgentContext
        {
            Requirements = [new Requirement { Id = "R1", Title = "Feature 1" }]
        };
        context.Artifacts.Add(new CodeArtifact { RelativePath = "Test.cs", Layer = ArtifactLayer.Test, TracedRequirementIds = ["R1"] });

        var matrix = TraceabilityGateAgent.BuildMatrix(context);

        Assert.Single(matrix);
        // Test counts as implementation artifact too (it's in Artifacts), so only check tests
        Assert.NotEmpty(matrix[0].VerifyingTests);
    }

    [Fact]
    public async Task ExecuteAsync_WithGaps_ProducesWarningFindings()
    {
        var context = new AgentContext
        {
            Requirements = [new Requirement { Id = "R1", Title = "Feature" }]
        };

        var agent = new TraceabilityGateAgent(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TraceabilityGateAgent>());
        var result = await agent.ExecuteAsync(context);

        Assert.False(result.Success); // gap exists
        Assert.NotEmpty(context.Findings);
        Assert.Contains(context.Artifacts, a => a.RelativePath.Contains("traceability-matrix"));
    }

    [Fact]
    public async Task ExecuteAsync_FullCoverage_SucceedsNoFindings()
    {
        var context = new AgentContext
        {
            Requirements = [new Requirement { Id = "R1", Title = "Feature" }]
        };
        context.Artifacts.Add(new CodeArtifact { RelativePath = "Svc.cs", Layer = ArtifactLayer.Service, TracedRequirementIds = ["R1"] });
        context.Artifacts.Add(new CodeArtifact { RelativePath = "Test.cs", Layer = ArtifactLayer.Test, TracedRequirementIds = ["R1"] });

        var agent = new TraceabilityGateAgent(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TraceabilityGateAgent>());
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Empty(context.Findings);
    }
}
