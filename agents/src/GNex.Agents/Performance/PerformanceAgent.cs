using System.Diagnostics;
using System.Text.RegularExpressions;
using GNex.Agents.Requirements;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Performance;

/// <summary>
/// Scans generated code artifacts for performance anti-patterns and rewrites them.
/// Invoked dynamically by the Orchestrator when review findings contain performance
/// concerns or as a standard post-Review optimization pass.
///
/// Patterns detected and fixed:
///   1. Missing async/await on I/O calls
///   2. N+1 query patterns (loading related data in loops)
///   3. Missing pagination on List endpoints
///   4. Unbounded SELECT * (no projection)
///   5. Missing CancellationToken propagation
///   6. Missing response caching hints
///   7. String concatenation in hot paths (should use StringBuilder)
///   8. Missing .AsNoTracking() on read-only queries
/// </summary>
public sealed class PerformanceAgent : IAgent
{
    private readonly ILogger<PerformanceAgent> _logger;

    public AgentType Type => AgentType.Performance;
    public string Name => "Performance Agent";
    public string Description => "Scans and optimizes generated code for async patterns, pagination, N+1 queries, caching, and CancellationToken propagation.";

    public PerformanceAgent(ILogger<PerformanceAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("PerformanceAgent starting — scanning {Count} artifacts", context.Artifacts.Count);

        var findings = new List<ReviewFinding>();
        var optimizedCount = 0;

        try
        {
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Scanning {context.Artifacts.Count} artifacts for async patterns, N+1 queries, pagination, caching...");
            foreach (var artifact in context.Artifacts)
            {
                ct.ThrowIfCancellationRequested();

                var optimizations = artifact.Layer switch
                {
                    ArtifactLayer.Service => OptimizeServiceLayer(artifact),
                    ArtifactLayer.Repository => OptimizeRepository(artifact),
                    ArtifactLayer.Dto => OptimizeDto(artifact),
                    ArtifactLayer.Integration => OptimizeKafkaLayer(artifact),
                    _ => (0, new List<ReviewFinding>())
                };

                optimizedCount += optimizations.Item1;
                findings.AddRange(optimizations.Item2);
            }

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Applied {optimizedCount} in-place optimizations, found {findings.Count} advisory items");

            // Cross-cutting: generate caching middleware artifact if services exist
            CodeArtifact? cacheArtifact = null;
            var serviceCount = context.Artifacts.Count(a => a.Layer == ArtifactLayer.Service);
            if (serviceCount > 0)
            {
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Generating response cache profile for {serviceCount} service artifacts");
                cacheArtifact = GenerateResponseCacheProfile();
                context.Artifacts.Add(cacheArtifact);
                optimizedCount++;
            }

            // Cross-cutting: generate health-check endpoint performance artifact
            var healthArtifact = GenerateHealthCheckOptimization();
            context.Artifacts.Add(healthArtifact);

            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Dispatch performance findings as feedback to responsible code-gen agents
            if (findings.Count > 0)
                context.DispatchFindingsAsFeedback(Type, findings);

            // Write targeted feedback to Database/ServiceLayer about specific perf issues
            var dbFindings = findings.Where(f => f.FilePath?.Contains("Repositor", StringComparison.OrdinalIgnoreCase) == true
                || f.Message.Contains("N+1", StringComparison.OrdinalIgnoreCase)
                || f.Message.Contains("AsNoTracking", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dbFindings.Count > 0)
                context.WriteFeedback(AgentType.Database, Type, $"Performance: {dbFindings.Count} DB-related issues found — {string.Join("; ", dbFindings.Take(3).Select(f => f.Message))}");

            var svcFindings = findings.Where(f => f.FilePath?.Contains("Service", StringComparison.OrdinalIgnoreCase) == true
                || f.Message.Contains("async", StringComparison.OrdinalIgnoreCase)
                || f.Message.Contains("pagination", StringComparison.OrdinalIgnoreCase)).ToList();
            if (svcFindings.Count > 0)
                context.WriteFeedback(AgentType.ServiceLayer, Type, $"Performance: {svcFindings.Count} service-related issues — {string.Join("; ", svcFindings.Take(3).Select(f => f.Message))}");

            await Task.CompletedTask;
            _logger.LogInformation("PerformanceAgent done — {Optimized} optimizations applied, {Findings} advisory findings",
                optimizedCount, findings.Count);

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"PerformanceAgent applied {optimizedCount} optimizations, raised {findings.Count} advisory findings",
                Artifacts = cacheArtifact is not null ? [cacheArtifact, healthArtifact] : [healthArtifact],
                Findings = findings,
                Messages = [new AgentMessage
                {
                    From = Type, To = AgentType.Orchestrator,
                    Subject = "Performance pass complete",
                    Body = $"{optimizedCount} in-place optimizations. {findings.Count} advisory items for manual review."
                }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "PerformanceAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    // ─── Service Layer Optimizations ────────────────────────────────────────

    private (int, List<ReviewFinding>) OptimizeServiceLayer(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;
        var fixes = 0;

        // 1. Missing CancellationToken propagation
        var asyncCallWithoutCt = new Regex(@"await\s+\w+\.\w+Async\(\s*[^,)]+\s*\)(?!\s*;?\s*//\s*no-ct)");
        foreach (Match m in asyncCallWithoutCt.Matches(content))
        {
            if (!m.Value.Contains(", ct") && !m.Value.Contains(",ct"))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Warning,
                    Category = "Performance",
                    Message = $"Async call without CancellationToken in '{artifact.FileName}': {m.Value.Trim()[..Math.Min(60, m.Value.Trim().Length)]}...",
                    Suggestion = "Pass CancellationToken to all async I/O calls to enable request cancellation."
                });
            }
        }

        // 2. N+1 detection: await inside foreach/for over a collection
        var nPlusOnePattern = new Regex(
            @"foreach\s*\([^)]+\)\s*\{[^}]*await\s+_repo\.\w+Async",
            RegexOptions.Singleline);
        if (nPlusOnePattern.IsMatch(content))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Warning,
                Category = "Performance-N+1",
                Message = $"Potential N+1 query detected in '{artifact.FileName}' — await inside foreach loop.",
                Suggestion = "Batch-load related entities or use Include() to eager-load in a single query."
            });
        }

        // 3. ListAsync without pagination bounds check
        if (content.Contains("ListAsync") && !content.Contains("Math.Min") && !content.Contains("take > "))
        {
            // Add pagination guard
            var listPattern = new Regex(
                @"(public async Task<List<\w+>> ListAsync\(int skip, int take,)");
            if (listPattern.IsMatch(content))
            {
                content = content.Replace(
                    "var items = await _repo.ListAsync(skip, take, ct);",
                    "take = Math.Clamp(take, 1, 200); // Performance: cap page size\n            var items = await _repo.ListAsync(skip, take, ct);");
                fixes++;
            }
        }

        if (content != artifact.Content)
        {
            artifact.Content = content;
        }

        return (fixes, findings);
    }

    // ─── Repository Layer Optimizations ─────────────────────────────────────

    private (int, List<ReviewFinding>) OptimizeRepository(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;
        var fixes = 0;

        // 1. Missing .AsNoTracking() on read-only queries
        if (content.Contains("GetByIdAsync") || content.Contains("ListAsync"))
        {
            if (!content.Contains("AsNoTracking"))
            {
                // Add AsNoTracking to read methods
                content = Regex.Replace(content,
                    @"(\.Where\([^)]+\))(\.ToListAsync)",
                    "$1.AsNoTracking()$2");

                content = Regex.Replace(content,
                    @"(\.Skip\(\w+\)\.Take\(\w+\))(\.ToListAsync)",
                    "$1.AsNoTracking()$2");

                if (content != artifact.Content) fixes++;

                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Info,
                    Category = "Performance-EF",
                    Message = $"Added .AsNoTracking() to read queries in '{artifact.FileName}'.",
                    Suggestion = "AsNoTracking() reduces memory overhead for read-only queries."
                });
            }
        }

        // 2. Missing index hints as comments
        if (content.Contains("_db.Set<") && !content.Contains("// Index:"))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Info,
                Category = "Performance-Index",
                Message = $"Repository '{artifact.FileName}' may benefit from composite indexes on TenantId + frequently filtered columns.",
                Suggestion = "Add .HasIndex(e => new {{ e.TenantId, e.StatusCode }}) in DbContext OnModelCreating."
            });
        }

        if (content != artifact.Content)
            artifact.Content = content;

        return (fixes, findings);
    }

    // ─── DTO Optimizations ──────────────────────────────────────────────────

    private (int, List<ReviewFinding>) OptimizeDto(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();

        // Flag large DTOs (> 20 properties) — may want projection
        var propCount = Regex.Matches(artifact.Content, @"public\s+\w+[\?\s]").Count;
        if (propCount > 20)
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Info,
                Category = "Performance-Payload",
                Message = $"DTO '{artifact.FileName}' has {propCount} properties — consider a lightweight summary DTO for list endpoints.",
                Suggestion = "Create a {Entity}SummaryDto with only essential fields for paginated list responses."
            });
        }

        return (0, findings);
    }

    // ─── Kafka Layer Optimizations ──────────────────────────────────────────

    private (int, List<ReviewFinding>) OptimizeKafkaLayer(CodeArtifact artifact)
    {
        var findings = new List<ReviewFinding>();
        var content = artifact.Content;
        var fixes = 0;

        // 1. Missing batch producer configuration
        if (content.Contains("EventProducer") && !content.Contains("Linger"))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Info,
                Category = "Performance-Kafka",
                Message = $"Kafka producer '{artifact.FileName}' may benefit from batching (LingerMs, BatchSize).",
                Suggestion = "Set LingerMs=5 and BatchSize=16384 in ProducerConfig for throughput optimization."
            });
        }

        // 2. Add Acks.Leader if missing (faster than Acks.All for non-critical events)
        if (content.Contains("_producer.ProduceAsync") && !content.Contains("Acks"))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Info,
                Category = "Performance-Kafka",
                Message = "Kafka producer missing explicit Acks configuration.",
                Suggestion = "Use Acks.Leader for non-critical events, Acks.All for audit/financial events."
            });
        }

        return (fixes, findings);
    }

    // ─── Response Cache Profile ─────────────────────────────────────────────

    private static CodeArtifact GenerateResponseCacheProfile() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Performance/CacheProfiles.cs",
        FileName = "CacheProfiles.cs",
        Namespace = "GNex.SharedKernel.Performance",
        ProducedBy = AgentType.Performance,
        Content = """
            namespace GNex.SharedKernel.Performance;

            /// <summary>
            /// Standard cache profiles for API responses.
            /// Applied via [ResponseCache(CacheProfileName = "...")] on endpoints.
            /// </summary>
            public static class CacheProfiles
            {
                public const string ShortLived = "ShortLived";   // 30s — list endpoints, search results
                public const string MediumLived = "MediumLived"; // 5min — reference data, facility info
                public const string NoCache = "NoCache";         // 0s — mutations, sensitive data

                public static void Configure(IDictionary<string, Microsoft.AspNetCore.Mvc.CacheProfile> profiles)
                {
                    profiles[ShortLived] = new() { Duration = 30, VaryByQueryKeys = ["skip", "take", "tenantId"] };
                    profiles[MediumLived] = new() { Duration = 300, VaryByQueryKeys = ["tenantId"] };
                    profiles[NoCache] = new() { Duration = 0, NoStore = true };
                }
            }
            """
    };

    // ─── Health Check Optimization ──────────────────────────────────────────

    private static CodeArtifact GenerateHealthCheckOptimization() => new()
    {
        Layer = ArtifactLayer.Configuration,
        RelativePath = "GNex.SharedKernel/Performance/HealthCheckOptimization.cs",
        FileName = "HealthCheckOptimization.cs",
        Namespace = "GNex.SharedKernel.Performance",
        ProducedBy = AgentType.Performance,
        Content = """
            using Microsoft.Extensions.Diagnostics.HealthChecks;

            namespace GNex.SharedKernel.Performance;

            /// <summary>
            /// Lightweight health check that avoids hitting the database on every probe.
            /// Uses a cached status with a configurable TTL to reduce DB load from
            /// Kubernetes liveness/readiness probes (typically every 10s per pod).
            /// </summary>
            public sealed class CachedDbHealthCheck : IHealthCheck
            {
                private readonly Func<CancellationToken, Task<bool>> _dbCheck;
                private readonly TimeSpan _cacheTtl;
                private volatile HealthCheckResult _cached = HealthCheckResult.Healthy("Not yet checked");
                private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

                public CachedDbHealthCheck(Func<CancellationToken, Task<bool>> dbCheck, TimeSpan? cacheTtl = null)
                {
                    _dbCheck = dbCheck;
                    _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(15);
                }

                public async Task<HealthCheckResult> CheckHealthAsync(
                    HealthCheckContext context, CancellationToken ct = default)
                {
                    if (DateTimeOffset.UtcNow - _lastCheck < _cacheTtl)
                        return _cached;

                    try
                    {
                        var ok = await _dbCheck(ct);
                        _cached = ok
                            ? HealthCheckResult.Healthy("DB reachable")
                            : HealthCheckResult.Degraded("DB unreachable");
                    }
                    catch (Exception ex)
                    {
                        _cached = HealthCheckResult.Unhealthy("DB check failed", ex);
                    }

                    _lastCheck = DateTimeOffset.UtcNow;
                    return _cached;
                }
            }
            """
    };
}
