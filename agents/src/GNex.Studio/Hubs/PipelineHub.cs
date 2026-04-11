using System.Collections.Concurrent;
using GNex.Agents.Orchestrator;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using GNex.Services.Platform;
using GNex.Studio.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace GNex.Studio.Hubs;

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
/// SignalR-based BRD status notifier — broadcasts BrdUpdated events to all clients.
/// </summary>
public sealed class SignalRBrdStatusNotifier(IHubContext<PipelineHub> hub) : IBrdStatusNotifier
{
    public async Task NotifyBrdStatusChangedAsync(string projectId, string status, string message, CancellationToken ct = default)
    {
        await hub.Clients.All.SendAsync("BrdUpdated", new { projectId, status, message }, ct);
    }
}

/// <summary>
/// Implements IPipelineEventSink by forwarding events to all SignalR clients,
/// persisting state to JSON snapshot, and recording events in the SQLite database.
/// Ensures pipeline run row exists before any FK-dependent inserts.
/// Incrementally persists backlog/requirements when key agents complete.
/// </summary>
public sealed class SignalRPipelineEventSink : IPipelineEventSink
{
    private readonly IHubContext<PipelineHub> _hub;
    private readonly PipelineStateStore _stateStore;
    private readonly AgentPipelineDb _db;
    private readonly IServiceProvider _sp;
    private readonly ConcurrentDictionary<string, bool> _runEnsured = new();
    private IAgentOrchestrator? _orchestrator;

    public SignalRPipelineEventSink(
        IHubContext<PipelineHub> hub,
        PipelineStateStore stateStore,
        AgentPipelineDb db,
        IServiceProvider sp)
    {
        _hub = hub;
        _stateStore = stateStore;
        _db = db;
        _sp = sp;
    }

    public async Task OnEventAsync(PipelineEvent evt, CancellationToken ct = default)
    {
        // Persist to JSON snapshot for quick page refresh
        _stateStore.TrackEvent(evt.RunId, (int)evt.Agent, (int)evt.Status,
            evt.Message, evt.ArtifactCount, evt.FindingCount, evt.ElapsedMs, evt.RetryAttempt);

        // Ensure pipeline run row exists before any FK-dependent inserts
        try
        {
            EnsureRunExists(evt.RunId);

            var agentName = ((AgentType)evt.Agent).ToString();
            var statusName = ((AgentStatus)evt.Status).ToString();
            _db.RecordAgentEvent(evt.RunId, agentName, statusName,
                evt.Message, evt.ArtifactCount, evt.FindingCount, evt.ElapsedMs, evt.RetryAttempt);

            // Incrementally persist state when key agents complete
            if (evt.Status == AgentStatus.Completed)
                PersistIncrementalState(evt.RunId, evt.Agent);
        }
        catch { /* best-effort — don't block pipeline for DB errors */ }

        await _hub.Clients.All.SendAsync("PipelineEvent", evt, ct);
    }

    /// <summary>Creates the PipelineRuns row if it doesn't exist yet (idempotent).</summary>
    private void EnsureRunExists(string runId)
    {
        if (_runEnsured.ContainsKey(runId)) return;
        try
        {
            _db.StartRun(runId, null, null);
        }
        catch { /* already exists — ignore */ }
        _runEnsured[runId] = true;
    }

    /// <summary>Persist backlog items and requirements to DB incrementally when key agents finish.</summary>
    private void PersistIncrementalState(string runId, AgentType agent)
    {
        _orchestrator ??= _sp.GetService<IAgentOrchestrator>();
        var ctx = _orchestrator?.GetCurrentContext();
        if (ctx is null || ctx.RunId != runId) return;

        try
        {
            // After RequirementsReader or RequirementsExpander — save requirements
            if (agent is AgentType.RequirementsReader or AgentType.RequirementsExpander && ctx.Requirements.Count > 0)
            {
                _db.SaveRequirements(runId, ctx.Requirements.Select(r => new RequirementRow
                {
                    Id = r.Id, ProjectId = ctx.ProjectId, SourceFile = r.SourceFile, Section = r.Section,
                    HeadingLevel = r.HeadingLevel, Title = r.Title, Description = r.Description,
                    Module = r.Module, Tags = r.Tags.ToList(),
                    AcceptanceCriteria = r.AcceptanceCriteria.ToList(),
                    DependsOn = r.DependsOn.ToList(), CreatedAt = DateTimeOffset.UtcNow
                }));
            }

            // After Backlog, RequirementsExpander, or any code-gen agent — save backlog items
            if (ctx.ExpandedRequirements.Count > 0 &&
                agent is AgentType.Backlog or AgentType.RequirementsExpander
                    or AgentType.Database or AgentType.ServiceLayer or AgentType.Application
                    or AgentType.Integration or AgentType.Testing or AgentType.Review
                    or AgentType.RequirementAnalyzer)
            {
                _db.SaveBacklogItems(runId, ctx.ExpandedRequirements.Select(e => new BacklogItemRow
                {
                    Id = e.Id, ProjectId = ctx.ProjectId, ParentId = e.ParentId, SourceRequirementId = e.SourceRequirementId,
                    ItemType = e.ItemType.ToString(), Status = e.Status.ToString(),
                    Title = e.Title, Description = e.Description, Module = e.Module,
                    Priority = e.Priority, Iteration = e.Iteration,
                    AcceptanceCriteria = e.AcceptanceCriteria, DependsOn = e.DependsOn, Tags = e.Tags,
                    TechnicalNotes = e.TechnicalNotes,
                    DefinitionOfDone = e.DefinitionOfDone,
                    DetailedSpec = e.DetailedSpec,
                    // Epic fields
                    Summary = e.Summary, BusinessValue = e.BusinessValue,
                    SuccessCriteria = e.SuccessCriteria, Scope = e.Scope,
                    // Story fields
                    StoryPoints = e.StoryPoints, Labels = e.Labels,
                    // Use Case fields
                    Actor = e.Actor, Preconditions = e.Preconditions,
                    MainFlow = e.MainFlow, AlternativeFlows = e.AlternativeFlows,
                    Postconditions = e.Postconditions,
                    // Bug fields
                    Severity = e.Severity, Environment = e.Environment,
                    StepsToReproduce = e.StepsToReproduce,
                    ExpectedResult = e.ExpectedResult, ActualResult = e.ActualResult,
                    // Gap-analysis fields
                    AffectedServices = e.AffectedServices, ProducedBy = e.ProducedBy,
                    Coverage = e.Coverage.ToString(),
                    CreatedAt = e.CreatedAt, StartedAt = e.StartedAt, CompletedAt = e.CompletedAt,
                    AssignedAgent = e.AssignedAgent
                }));
            }
        }
        catch { /* best-effort */ }
    }
}
