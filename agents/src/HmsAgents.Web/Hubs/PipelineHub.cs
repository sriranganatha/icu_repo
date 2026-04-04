using HmsAgents.Agents.Orchestrator;
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
/// Implements IPipelineEventSink by forwarding events to all SignalR clients.
/// </summary>
public sealed class SignalRPipelineEventSink : IPipelineEventSink
{
    private readonly IHubContext<PipelineHub> _hub;

    public SignalRPipelineEventSink(IHubContext<PipelineHub> hub) => _hub = hub;

    public async Task OnEventAsync(PipelineEvent evt, CancellationToken ct = default)
    {
        await _hub.Clients.All.SendAsync("PipelineEvent", evt, ct);
    }
}
