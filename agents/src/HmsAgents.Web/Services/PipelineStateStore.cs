using System.Text.Json;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Web.Services;

/// <summary>
/// Persists pipeline run state to disk so the dashboard survives page refreshes and server restarts.
/// </summary>
public sealed class PipelineStateStore
{
    private static readonly string s_statePath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "pipeline-state.json");

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();

    public PipelineStateSnapshot? CurrentSnapshot { get; private set; }

    public PipelineStateStore()
    {
        // Load from disk on startup
        CurrentSnapshot = LoadFromDisk();
    }

    /// <summary>
    /// Update agent status from a pipeline event and persist.
    /// </summary>
    public void TrackEvent(string runId, int agent, int status, string message,
        int artifactCount, int findingCount, double elapsedMs, int retryAttempt)
    {
        lock (_lock)
        {
            var snapshot = CurrentSnapshot ??= new PipelineStateSnapshot();
            snapshot.RunId = runId;
            snapshot.LastUpdated = DateTimeOffset.UtcNow;

            var agentName = ((AgentType)agent).ToString();
            snapshot.AgentStatuses[agentName] = ((AgentStatus)status).ToString();
            snapshot.AgentMessages[agentName] = message;
            snapshot.AgentElapsed[agentName] = elapsedMs;

            if (artifactCount > 0)
                snapshot.AgentArtifacts[agentName] = artifactCount;
            if (findingCount > 0)
                snapshot.AgentFindings[agentName] = findingCount;
            if (retryAttempt > 0)
                snapshot.AgentRetries[agentName] = retryAttempt;

            SaveToDisk(snapshot);
        }
    }

    /// <summary>
    /// Save final pipeline completion stats + backlog items.
    /// </summary>
    public void TrackCompletion(string runId, int requirementCount, int artifactCount,
        int findingCount, int testDiagnosticCount, List<ExpandedRequirement> backlogItems, double durationMs)
    {
        lock (_lock)
        {
            var snapshot = CurrentSnapshot ??= new PipelineStateSnapshot();
            snapshot.RunId = runId;
            snapshot.Completed = true;
            snapshot.CompletedAt = DateTimeOffset.UtcNow;
            snapshot.RequirementCount = requirementCount;
            snapshot.ArtifactCount = artifactCount;
            snapshot.FindingCount = findingCount;
            snapshot.TestDiagnosticCount = testDiagnosticCount;
            snapshot.BacklogCount = backlogItems.Count;
            snapshot.DurationMs = durationMs;
            snapshot.LastUpdated = DateTimeOffset.UtcNow;

            // Persist backlog items
            snapshot.BacklogItems = backlogItems.Select(e => new BacklogItemSnapshot
            {
                Id = e.Id,
                ParentId = e.ParentId,
                SourceRequirementId = e.SourceRequirementId,
                ItemType = e.ItemType.ToString(),
                Status = e.Status.ToString(),
                Title = e.Title,
                Description = e.Description,
                Module = e.Module,
                Priority = e.Priority,
                Iteration = e.Iteration,
                Tags = e.Tags,
                AcceptanceCriteria = e.AcceptanceCriteria,
                DependsOn = e.DependsOn,
                CreatedAt = e.CreatedAt,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
                AssignedAgent = e.AssignedAgent
            }).ToList();

            SaveToDisk(snapshot);
        }
    }

    /// <summary>
    /// Clear state for a new run.
    /// </summary>
    public void Reset(string runId)
    {
        lock (_lock)
        {
            CurrentSnapshot = new PipelineStateSnapshot { RunId = runId };
            SaveToDisk(CurrentSnapshot);
        }
    }

    private PipelineStateSnapshot? LoadFromDisk()
    {
        try
        {
            var path = Path.GetFullPath(s_statePath);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PipelineStateSnapshot>(json, s_jsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private void SaveToDisk(PipelineStateSnapshot snapshot)
    {
        try
        {
            var path = Path.GetFullPath(s_statePath);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, s_jsonOpts));
        }
        catch
        {
            // Best-effort — don't crash the pipeline for state persistence
        }
    }
}

public sealed class PipelineStateSnapshot
{
    public string RunId { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    public int RequirementCount { get; set; }
    public int ArtifactCount { get; set; }
    public int FindingCount { get; set; }
    public int TestDiagnosticCount { get; set; }
    public int BacklogCount { get; set; }
    public double DurationMs { get; set; }

    public Dictionary<string, string> AgentStatuses { get; set; } = new();
    public Dictionary<string, string> AgentMessages { get; set; } = new();
    public Dictionary<string, double> AgentElapsed { get; set; } = new();
    public Dictionary<string, int> AgentArtifacts { get; set; } = new();
    public Dictionary<string, int> AgentFindings { get; set; } = new();
    public Dictionary<string, int> AgentRetries { get; set; } = new();
    public List<BacklogItemSnapshot> BacklogItems { get; set; } = [];
}

public sealed class BacklogItemSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public string SourceRequirementId { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int Iteration { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> AcceptanceCriteria { get; set; } = [];
    public List<string> DependsOn { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string AssignedAgent { get; set; } = string.Empty;
}
