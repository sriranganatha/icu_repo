using System.Collections.Concurrent;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using GNex.Studio.Hubs;

namespace GNex.Studio.Services;

/// <summary>
/// Human-in-the-loop gate. When an agent requests approval, the gate:
/// 1. Persists the request to SQLite
/// 2. Broadcasts a SignalR event to the dashboard
/// 3. Blocks the agent (via SemaphoreSlim) until a human responds or timeout expires
/// </summary>
public sealed class HumanGate : IHumanGate
{
    private readonly AgentPipelineDb _db;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly IAuditLogger _audit;
    private readonly ILogger<HumanGate> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<HumanDecision>> _pending = new();

    public HumanGate(AgentPipelineDb db, IHubContext<PipelineHub> hub, IAuditLogger audit, ILogger<HumanGate> logger)
    {
        _db = db;
        _hub = hub;
        _audit = audit;
        _logger = logger;
    }

    public async Task<HumanDecision> RequestApprovalAsync(HumanApprovalRequest request, CancellationToken ct = default)
    {
        // Persist to DB
        _db.InsertHumanDecision(
            request.Id,
            request.RunId,
            request.RequestingAgent.ToString(),
            request.Category.ToString(),
            request.Title,
            request.Description,
            request.Details,
            request.Timeout.TotalMinutes);

        // Create a completion source the agent will wait on
        var tcs = new TaskCompletionSource<HumanDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;

        // Broadcast to dashboard
        await _hub.Clients.All.SendAsync("HumanApprovalRequested", new
        {
            request.Id,
            request.RunId,
            agent = request.RequestingAgent.ToString(),
            category = request.Category.ToString(),
            request.Title,
            request.Description,
            request.Details,
            request.RequestedAt,
            timeoutMinutes = request.Timeout.TotalMinutes
        }, ct);

        // Audit the request
        await _audit.LogAsync(request.RequestingAgent, request.RunId,
            AuditAction.HumanApprovalRequested,
            $"Human approval requested: {request.Title}",
            $"Category: {request.Category}. {request.Description}",
            severity: AuditSeverity.Decision, ct: ct);

        _logger.LogWarning("[HITL] Approval requested by {Agent}: {Title} — waiting up to {Timeout}m",
            request.RequestingAgent, request.Title, request.Timeout.TotalMinutes);

        // Wait for human response with timeout
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(request.Timeout);

            var decision = await tcs.Task.WaitAsync(cts.Token);
            _pending.TryRemove(request.Id, out _);
            request.Decision = decision;
            return decision;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — auto-reject
            _pending.TryRemove(request.Id, out _);
            _db.UpdateHumanDecision(request.Id, "TimedOut", "Timed out waiting for human response");

            await _audit.LogAsync(request.RequestingAgent, request.RunId,
                AuditAction.HumanRejected,
                $"Human approval timed out: {request.Title}",
                severity: AuditSeverity.Warning, ct: default);

            await _hub.Clients.All.SendAsync("HumanDecisionMade", new
            {
                request.Id,
                decision = "TimedOut",
                reason = "Timed out waiting for human response"
            }, default);

            request.Decision = HumanDecision.TimedOut;
            return HumanDecision.TimedOut;
        }
    }

    public async Task SubmitDecisionAsync(string requestId, bool approved, string? reason = null, CancellationToken ct = default)
    {
        var decisionStr = approved ? "Approved" : "Rejected";
        _db.UpdateHumanDecision(requestId, decisionStr, reason);

        // Unblock the waiting agent
        if (_pending.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetResult(approved ? HumanDecision.Approved : HumanDecision.Rejected);
        }

        // Get the request details for audit
        var row = _db.GetDecision(requestId);
        if (row is not null)
        {
            var agentType = Enum.TryParse<AgentType>(row.RequestingAgent, out var at) ? at : AgentType.Orchestrator;

            await _audit.LogAsync(agentType, row.RunId,
                approved ? AuditAction.HumanApproved : AuditAction.HumanRejected,
                $"Human {decisionStr.ToLowerInvariant()}: {row.Title}",
                reason,
                severity: AuditSeverity.Decision, ct: ct);
        }

        // Broadcast decision to dashboard
        await _hub.Clients.All.SendAsync("HumanDecisionMade", new
        {
            id = requestId,
            decision = decisionStr,
            reason
        }, ct);

        _logger.LogInformation("[HITL] Decision {Decision} for request {Id}: {Reason}", decisionStr, requestId, reason ?? "—");
    }

    public Task<List<HumanApprovalRequest>> GetPendingRequestsAsync(CancellationToken ct = default)
    {
        var rows = _db.GetPendingDecisions();
        return Task.FromResult(rows.Select(MapRow).ToList());
    }

    public Task<List<HumanApprovalRequest>> GetDecisionHistoryAsync(string runId, CancellationToken ct = default)
    {
        var rows = _db.GetDecisionHistory(runId);
        return Task.FromResult(rows.Select(MapRow).ToList());
    }

    private static HumanApprovalRequest MapRow(HumanDecisionRow r) => new()
    {
        Id = r.Id,
        RunId = r.RunId,
        RequestingAgent = Enum.TryParse<AgentType>(r.RequestingAgent, out var at) ? at : AgentType.Orchestrator,
        Category = Enum.TryParse<HumanGateCategory>(r.Category, out var cat) ? cat : HumanGateCategory.ConfigurationChange,
        Title = r.Title,
        Description = r.Description,
        Details = r.Details,
        Decision = Enum.TryParse<HumanDecision>(r.Decision, out var d) ? d : HumanDecision.Pending,
        DecisionReason = r.DecisionReason,
        RequestedAt = DateTimeOffset.TryParse(r.RequestedAt, out var ra) ? ra : DateTimeOffset.UtcNow,
        DecidedAt = string.IsNullOrEmpty(r.DecidedAt) ? null : DateTimeOffset.TryParse(r.DecidedAt, out var da) ? da : null,
        Timeout = TimeSpan.FromMinutes(r.TimeoutMinutes)
    };
}
