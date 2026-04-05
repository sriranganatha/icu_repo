using System.Security.Cryptography;
using System.Text;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;

namespace HmsAgents.Web.Services;

/// <summary>
/// Verifiable hash-chained audit logger. Each entry's SHA-256 hash includes the
/// previous entry's hash, creating a blockchain-like tamper-evident audit trail.
/// Backed by SQLite for persistence; broadcasts new entries via SignalR.
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly AgentPipelineDb _db;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(AgentPipelineDb db, ILogger<AuditLogger> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            var (seq, hash) = _db.AppendAuditEntry(
                entry.Id,
                entry.RunId,
                entry.Agent.ToString(),
                entry.Action.ToString(),
                entry.Severity.ToString(),
                entry.Description,
                entry.Details,
                entry.InputHash,
                entry.OutputHash,
                entry.Timestamp);

            entry.Sequence = seq;
            entry.EntryHash = hash;

            _logger.LogDebug("[AUDIT #{Seq}] {Agent}/{Action}: {Description}", seq, entry.Agent, entry.Action, entry.Description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log write failed for {Agent}/{Action}", entry.Agent, entry.Action);
        }
        return Task.CompletedTask;
    }

    public Task LogAsync(AgentType agent, string runId, AuditAction action, string description,
        string? details = null, string? inputHash = null, string? outputHash = null,
        AuditSeverity severity = AuditSeverity.Info, CancellationToken ct = default)
    {
        return LogAsync(new AuditEntry
        {
            RunId = runId,
            Agent = agent,
            Action = action,
            Severity = severity,
            Description = description,
            Details = details,
            InputHash = inputHash,
            OutputHash = outputHash
        }, ct);
    }

    public Task<(bool IsValid, int? BrokenAtSequence)> VerifyChainAsync(string runId, CancellationToken ct = default)
    {
        var result = _db.VerifyAuditChain(runId);
        return Task.FromResult(result);
    }

    /// <summary>Compute SHA-256 hash of content for input/output hashing.</summary>
    public static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
