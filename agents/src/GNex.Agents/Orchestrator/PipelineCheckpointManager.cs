using System.Collections.Concurrent;
using System.Text.Json;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Agents.Orchestrator;

/// <summary>
/// Records pipeline checkpoints and supports replay/resume from a checkpoint.
/// </summary>
public sealed class PipelineCheckpointManager
{
    private int _sequence;
    private readonly ConcurrentBag<PipelineCheckpoint> _checkpoints;

    public PipelineCheckpointManager(ConcurrentBag<PipelineCheckpoint> checkpoints)
    {
        _checkpoints = checkpoints;
    }

    public PipelineCheckpoint Record(
        string runId,
        CheckpointType type,
        string agentName,
        IEnumerable<string> completedAgents,
        IEnumerable<string> pendingAgents,
        Dictionary<string, string>? state = null)
    {
        var cp = new PipelineCheckpoint
        {
            RunId = runId,
            SequenceNumber = Interlocked.Increment(ref _sequence),
            Type = type,
            AgentName = agentName,
            CompletedAgents = completedAgents.ToList(),
            PendingAgents = pendingAgents.ToList(),
            State = state ?? []
        };
        _checkpoints.Add(cp);
        return cp;
    }

    public PipelineCheckpoint? GetLatest(string runId) =>
        _checkpoints
            .Where(c => c.RunId == runId)
            .OrderByDescending(c => c.SequenceNumber)
            .FirstOrDefault();

    public List<PipelineCheckpoint> GetAll(string runId) =>
        [.. _checkpoints
            .Where(c => c.RunId == runId)
            .OrderBy(c => c.SequenceNumber)];

    /// <summary>
    /// Returns the set of agents that were completed at the latest checkpoint,
    /// so the orchestrator can skip them on resume.
    /// </summary>
    public HashSet<AgentType> GetCompletedAgentsAtCheckpoint(string runId)
    {
        var latest = GetLatest(runId);
        if (latest is null) return [];
        var result = new HashSet<AgentType>();
        foreach (var name in latest.CompletedAgents)
        {
            if (Enum.TryParse<AgentType>(name, out var at))
                result.Add(at);
        }
        return result;
    }

    /// <summary>
    /// Exports all checkpoints as JSON for persistence / diagnostics.
    /// </summary>
    public string ExportJson(string runId)
    {
        var all = GetAll(runId);
        return JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
    }
}
