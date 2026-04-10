using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

public class LearningLoopTests
{
    [Fact]
    public void RecordRun_AddsRecordToContext()
    {
        var context = new AgentContext();
        var result = new AgentResult { Agent = AgentType.Database, Success = true, Duration = TimeSpan.FromSeconds(5) };

        LearningLoopAgent.RecordRun(context, AgentType.Database, result);

        Assert.Single(context.LearningRecords);
        var record = context.LearningRecords.First();
        Assert.Equal("Database", record.AgentType);
        Assert.True(record.Succeeded);
    }

    [Fact]
    public void Analyze_NoRecords_ReturnsDefaultRecommendation()
    {
        var context = new AgentContext();
        var recs = LearningLoopAgent.Analyze([], context);

        Assert.Single(recs);
        Assert.Contains("No agent run records", recs[0]);
    }

    [Fact]
    public void Analyze_SlowAgent_ProducesPerformanceRec()
    {
        var context = new AgentContext();
        var records = new List<AgentLearningRecord>
        {
            new() { AgentType = "Database", Succeeded = true, Duration = TimeSpan.FromMinutes(5), ArtifactsProduced = 3 }
        };

        var recs = LearningLoopAgent.Analyze(records, context);

        Assert.Contains(recs, r => r.Contains("PERF") && r.Contains("Database"));
    }

    [Fact]
    public void Analyze_HighRetryAgent_ProducesReliabilityRec()
    {
        var context = new AgentContext();
        var records = new List<AgentLearningRecord>
        {
            new() { AgentType = "ServiceLayer", Succeeded = true, RetryCount = 3, Duration = TimeSpan.FromSeconds(10), ArtifactsProduced = 1 },
            new() { AgentType = "ServiceLayer", Succeeded = true, RetryCount = 4, Duration = TimeSpan.FromSeconds(10), ArtifactsProduced = 1 }
        };

        var recs = LearningLoopAgent.Analyze(records, context);

        Assert.Contains(recs, r => r.Contains("RELIABILITY"));
    }

    [Fact]
    public void Analyze_NoArtifactsAgent_ProducesOutputRec()
    {
        var context = new AgentContext();
        var records = new List<AgentLearningRecord>
        {
            new() { AgentType = "Reviewer", Succeeded = true, Duration = TimeSpan.FromSeconds(5), ArtifactsProduced = 0 }
        };

        var recs = LearningLoopAgent.Analyze(records, context);

        Assert.Contains(recs, r => r.Contains("OUTPUT"));
    }

    [Fact]
    public void Analyze_HighFailureRate_ProducesFailureRec()
    {
        var context = new AgentContext();
        var records = new List<AgentLearningRecord>
        {
            new() { AgentType = "Build", Succeeded = false, Duration = TimeSpan.FromSeconds(5) },
            new() { AgentType = "Build", Succeeded = false, Duration = TimeSpan.FromSeconds(5) },
            new() { AgentType = "Build", Succeeded = true, Duration = TimeSpan.FromSeconds(5), ArtifactsProduced = 1 }
        };

        var recs = LearningLoopAgent.Analyze(records, context);

        Assert.Contains(recs, r => r.Contains("FAILURE") && r.Contains("Build"));
    }

    [Fact]
    public async Task ExecuteAsync_ProducesLearningReportArtifact()
    {
        var context = new AgentContext();
        LearningLoopAgent.RecordRun(context, AgentType.Database,
            new AgentResult { Agent = AgentType.Database, Success = true, Duration = TimeSpan.FromSeconds(2) });

        var agent = new LearningLoopAgent(new Microsoft.Extensions.Logging.Abstractions.NullLogger<LearningLoopAgent>());
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Contains(context.Artifacts, a => a.RelativePath.Contains("learning-report"));
    }
}
