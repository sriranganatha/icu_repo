using HmsAgents.Agents.Orchestrator;
using HmsAgents.Core.Enums;
using HmsAgents.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace HmsAgents.Web.Hubs;

/// <summary>
/// Pushes real-time pipeline events to connected dashboard clients.
/// </summary>
public sealed class PipelineHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", "Dashboard connected to pipeline hub.");
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Implements IPipelineEventSink by forwarding events to all SignalR clients,
/// persisting state to JSON snapshot, and recording events in the SQLite database.
/// </summary>
public sealed class SignalRPipelineEventSink : IPipelineEventSink
{
    private readonly IHubContext<PipelineHub> _hub;
    private readonly PipelineStateStore _stateStore;
    private readonly AgentPipelineDb _db;

    public SignalRPipelineEventSink(IHubContext<PipelineHub> hub, PipelineStateStore stateStore, AgentPipelineDb db)
    {
        _hub = hub;
        _stateStore = stateStore;
        _db = db;
    }

    public async Task OnEventAsync(PipelineEvent evt, CancellationToken ct = default)
    {
        // Persist to JSON snapshot for quick page refresh
        _stateStore.TrackEvent(evt.RunId, (int)evt.Agent, (int)evt.Status,
            evt.Message, evt.ArtifactCount, evt.FindingCount, evt.ElapsedMs, evt.RetryAttempt);

        // Record in SQLite DB for disaster recovery
        try
        {
            var agentName = ((AgentType)evt.Agent).ToString();
            var statusName = ((AgentStatus)evt.Status).ToString();
            _db.RecordAgentEvent(evt.RunId, agentName, statusName,
                evt.Message, evt.ArtifactCount, evt.FindingCount, evt.ElapsedMs, evt.RetryAttempt);
        }
        catch { /* best-effort — don't block pipeline for DB errors */ }

        await _hub.Clients.All.SendAsync("PipelineEvent", evt, ct);
    }
}
