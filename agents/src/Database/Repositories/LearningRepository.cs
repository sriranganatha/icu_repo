using Microsoft.EntityFrameworkCore;
using GNex.Database.Entities.Platform.AgentRegistry;

namespace GNex.Database.Repositories;

/// <summary>
/// Repository for persisting and retrieving agent learning records.
/// Supports three-tier loading: Project → Domain → Global.
/// Auto-promotes learnings when recurrence crosses thresholds.
/// </summary>
public interface ILearningRepository
{
    /// <summary>Load all active learnings for a project, ordered by recurrence (most common first).</summary>
    Task<List<AgentLearning>> GetByProjectAsync(string projectId, int maxItems = 100, CancellationToken ct = default);

    /// <summary>Load learnings targeting a specific agent type for a project.</summary>
    Task<List<AgentLearning>> GetForAgentAsync(string projectId, string agentTypeCode, int maxItems = 50, CancellationToken ct = default);

    /// <summary>Load Domain-scope learnings for a domain (apply to all projects in that domain).</summary>
    Task<List<AgentLearning>> GetByDomainAsync(string domain, int maxItems = 50, CancellationToken ct = default);

    /// <summary>Load Global-scope learnings (apply universally to all projects in all domains).</summary>
    Task<List<AgentLearning>> GetGlobalAsync(int maxItems = 50, CancellationToken ct = default);

    /// <summary>
    /// Load combined learnings for a pipeline run: project-specific + domain-wide + global.
    /// This is the primary method used by the orchestrator at pipeline start.
    /// </summary>
    Task<List<AgentLearning>> GetCombinedForPipelineAsync(string projectId, string domain, int maxItems = 150, CancellationToken ct = default);

    /// <summary>Save a batch of new learnings. Deduplicates by Problem+AgentType and auto-promotes scope.</summary>
    Task SaveBatchAsync(IEnumerable<AgentLearning> learnings, CancellationToken ct = default);

    /// <summary>Mark a learning as verified (the fix was confirmed by a clean pipeline run).</summary>
    Task VerifyAsync(string learningId, CancellationToken ct = default);

    /// <summary>Deprecate a learning (the underlying issue no longer applies).</summary>
    Task DeprecateAsync(string learningId, CancellationToken ct = default);
}

public class LearningRepository : ILearningRepository
{
    private readonly GNexDbContext _db;

    // Promotion thresholds
    private const int PromoteToDomainThreshold = 2;   // seen in 2+ projects in same domain → Domain scope
    private const int PromoteToGlobalThreshold = 3;    // seen in 3+ projects OR 2+ domains → Global scope

    public LearningRepository(GNexDbContext db) => _db = db;

    public async Task<List<AgentLearning>> GetByProjectAsync(string projectId, int maxItems = 100, CancellationToken ct = default)
        => await _db.AgentLearnings
            .Where(l => l.ProjectId == projectId && l.IsActive && !l.IsDeprecated)
            .OrderByDescending(l => l.Confidence)
            .ThenByDescending(l => l.Recurrence)
            .Take(maxItems)
            .ToListAsync(ct);

    public async Task<List<AgentLearning>> GetForAgentAsync(string projectId, string agentTypeCode, int maxItems = 50, CancellationToken ct = default)
        => await _db.AgentLearnings
            .Where(l => l.ProjectId == projectId && l.IsActive && !l.IsDeprecated &&
                        (l.AgentTypeCode == agentTypeCode || l.TargetAgents.Contains(agentTypeCode)))
            .OrderByDescending(l => l.Confidence)
            .ThenByDescending(l => l.Recurrence)
            .Take(maxItems)
            .ToListAsync(ct);

    public async Task<List<AgentLearning>> GetByDomainAsync(string domain, int maxItems = 50, CancellationToken ct = default)
        => await _db.AgentLearnings
            .Where(l => l.Domain == domain && l.IsActive && !l.IsDeprecated && l.Scope >= 1) // Domain or Global
            .OrderByDescending(l => l.Confidence)
            .ThenByDescending(l => l.Recurrence)
            .Take(maxItems)
            .ToListAsync(ct);

    public async Task<List<AgentLearning>> GetGlobalAsync(int maxItems = 50, CancellationToken ct = default)
        => await _db.AgentLearnings
            .Where(l => l.IsActive && !l.IsDeprecated && l.Scope == 2) // Global only
            .OrderByDescending(l => l.Confidence)
            .ThenByDescending(l => l.Recurrence)
            .Take(maxItems)
            .ToListAsync(ct);

    public async Task<List<AgentLearning>> GetCombinedForPipelineAsync(
        string projectId, string domain, int maxItems = 150, CancellationToken ct = default)
    {
        // Load all three tiers in parallel
        var projectTask = GetByProjectAsync(projectId, maxItems / 3, ct);
        var domainTask = string.IsNullOrEmpty(domain)
            ? Task.FromResult(new List<AgentLearning>())
            : GetByDomainAsync(domain, maxItems / 3, ct);
        var globalTask = GetGlobalAsync(maxItems / 3, ct);

        await Task.WhenAll(projectTask, domainTask, globalTask);

        // Merge and deduplicate by Problem (prefer higher scope)
        var combined = new Dictionary<string, AgentLearning>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in globalTask.Result) combined.TryAdd(NormalizeKey(l), l);
        foreach (var l in domainTask.Result) combined.TryAdd(NormalizeKey(l), l);
        foreach (var l in projectTask.Result) combined.TryAdd(NormalizeKey(l), l);

        return combined.Values
            .OrderByDescending(l => l.Scope)
            .ThenByDescending(l => l.Confidence)
            .ThenByDescending(l => l.Recurrence)
            .Take(maxItems)
            .ToList();
    }

    public async Task SaveBatchAsync(IEnumerable<AgentLearning> learnings, CancellationToken ct = default)
    {
        foreach (var learning in learnings)
        {
            // Check for existing learning with same problem fingerprint
            var normalizedProblem = learning.Problem.Trim().ToUpperInvariant();
            var existing = await _db.AgentLearnings.FirstOrDefaultAsync(l =>
                l.AgentTypeCode == learning.AgentTypeCode &&
                l.Problem.ToUpper() == normalizedProblem &&
                l.IsActive && !l.IsDeprecated, ct);

            if (existing is not null)
            {
                // Increment recurrence
                existing.Recurrence++;
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                // Track cross-project spread
                if (!string.IsNullOrEmpty(learning.ProjectId) && !existing.SeenInProjects.Contains(learning.ProjectId))
                    existing.SeenInProjects = string.IsNullOrEmpty(existing.SeenInProjects)
                        ? learning.ProjectId
                        : $"{existing.SeenInProjects},{learning.ProjectId}";

                if (!string.IsNullOrEmpty(learning.Domain) && !existing.SeenInDomains.Contains(learning.Domain))
                    existing.SeenInDomains = string.IsNullOrEmpty(existing.SeenInDomains)
                        ? learning.Domain
                        : $"{existing.SeenInDomains},{learning.Domain}";

                // Update resolution if better
                if (!string.IsNullOrWhiteSpace(learning.Resolution) && learning.Resolution.Length > existing.Resolution.Length)
                    existing.Resolution = learning.Resolution;
                if (!string.IsNullOrWhiteSpace(learning.PromptRule) && learning.PromptRule.Length > existing.PromptRule.Length)
                    existing.PromptRule = learning.PromptRule;

                // Auto-promote scope based on spread
                AutoPromoteScope(existing);
                RecalculateConfidence(existing);
            }
            else
            {
                learning.CreatedAt = DateTimeOffset.UtcNow;
                if (string.IsNullOrEmpty(learning.TenantId))
                    learning.TenantId = _db.CurrentTenantId;
                if (!string.IsNullOrEmpty(learning.ProjectId))
                    learning.SeenInProjects = learning.ProjectId;
                if (!string.IsNullOrEmpty(learning.Domain))
                    learning.SeenInDomains = learning.Domain;
                RecalculateConfidence(learning);
                _db.AgentLearnings.Add(learning);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task VerifyAsync(string learningId, CancellationToken ct = default)
    {
        var learning = await _db.AgentLearnings.FindAsync([learningId], ct);
        if (learning is null) return;
        learning.IsVerified = true;
        learning.UpdatedAt = DateTimeOffset.UtcNow;
        RecalculateConfidence(learning);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeprecateAsync(string learningId, CancellationToken ct = default)
    {
        var learning = await _db.AgentLearnings.FindAsync([learningId], ct);
        if (learning is null) return;
        learning.IsDeprecated = true;
        learning.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string NormalizeKey(AgentLearning l)
        => $"{l.AgentTypeCode}::{l.Problem.Trim().ToUpperInvariant()}";

    private static void AutoPromoteScope(AgentLearning l)
    {
        var projectCount = string.IsNullOrEmpty(l.SeenInProjects) ? 0 : l.SeenInProjects.Split(',').Length;
        var domainCount = string.IsNullOrEmpty(l.SeenInDomains) ? 0 : l.SeenInDomains.Split(',').Length;

        if (projectCount >= PromoteToGlobalThreshold || domainCount >= 2)
            l.Scope = 2; // Global
        else if (projectCount >= PromoteToDomainThreshold)
            l.Scope = 1; // Domain
    }

    private static void RecalculateConfidence(AgentLearning l)
    {
        var projectCount = string.IsNullOrEmpty(l.SeenInProjects) ? 0 : l.SeenInProjects.Split(',').Length;
        var domainCount = string.IsNullOrEmpty(l.SeenInDomains) ? 0 : l.SeenInDomains.Split(',').Length;

        var score = 0.3; // base
        if (l.IsVerified) score += 0.3;
        score += Math.Min(0.2, l.Recurrence * 0.04);
        score += Math.Min(0.1, projectCount * 0.05);
        score += Math.Min(0.1, domainCount * 0.05);
        l.Confidence = Math.Min(1.0, score);
    }
}
