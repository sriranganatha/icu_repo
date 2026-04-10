using System.Collections.Concurrent;
using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

public class PipelineCheckpointTests
{
    [Fact]
    public void Record_CreatesCheckpointWithSequence()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        var cp = mgr.Record("run1", CheckpointType.AgentStarted, "Database", ["RequirementsReader"], ["Database", "ServiceLayer"]);

        Assert.Equal("run1", cp.RunId);
        Assert.Equal(1, cp.SequenceNumber);
        Assert.Equal(CheckpointType.AgentStarted, cp.Type);
        Assert.Equal("Database", cp.AgentName);
    }

    [Fact]
    public void Record_SequenceIncrementsAcrossCalls()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        mgr.Record("run1", CheckpointType.AgentStarted, "A", [], []);
        var cp2 = mgr.Record("run1", CheckpointType.AgentCompleted, "A", ["A"], []);

        Assert.Equal(2, cp2.SequenceNumber);
    }

    [Fact]
    public void GetLatest_ReturnsHighestSequence()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        mgr.Record("run1", CheckpointType.AgentStarted, "A", [], []);
        mgr.Record("run1", CheckpointType.AgentCompleted, "A", ["A"], []);
        mgr.Record("run1", CheckpointType.AgentStarted, "B", ["A"], ["B"]);

        var latest = mgr.GetLatest("run1");
        Assert.NotNull(latest);
        Assert.Equal(3, latest.SequenceNumber);
        Assert.Equal("B", latest.AgentName);
    }

    [Fact]
    public void GetLatest_DifferentRunIds_AreIsolated()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        mgr.Record("run1", CheckpointType.AgentCompleted, "A", ["A"], []);
        mgr.Record("run2", CheckpointType.AgentCompleted, "B", ["B"], []);

        var latest1 = mgr.GetLatest("run1");
        Assert.Equal("A", latest1!.AgentName);

        var latest2 = mgr.GetLatest("run2");
        Assert.Equal("B", latest2!.AgentName);
    }

    [Fact]
    public void GetAll_ReturnsOrderedBySequence()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        mgr.Record("r", CheckpointType.AgentStarted, "A", [], []);
        mgr.Record("r", CheckpointType.AgentCompleted, "A", ["A"], []);
        mgr.Record("r", CheckpointType.WaveCompleted, "Wave1", ["A"], ["B"]);

        var all = mgr.GetAll("r");
        Assert.Equal(3, all.Count);
        Assert.Equal(1, all[0].SequenceNumber);
        Assert.Equal(3, all[2].SequenceNumber);
    }

    [Fact]
    public void GetCompletedAgentsAtCheckpoint_ParsesEnumValues()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        mgr.Record("r", CheckpointType.AgentCompleted, "DB", ["Database", "ServiceLayer"], []);

        var completed = mgr.GetCompletedAgentsAtCheckpoint("r");
        Assert.Contains(AgentType.Database, completed);
        Assert.Contains(AgentType.ServiceLayer, completed);
    }

    [Fact]
    public void ExportJson_ProducesValidJson()
    {
        var bag = new ConcurrentBag<PipelineCheckpoint>();
        var mgr = new PipelineCheckpointManager(bag);

        mgr.Record("r", CheckpointType.AgentStarted, "Test", [], []);

        var json = mgr.ExportJson("r");
        Assert.Contains("\"RunId\"", json);
        Assert.Contains("\"Test\"", json);
    }
}
