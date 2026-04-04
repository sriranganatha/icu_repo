using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Review;

public sealed class ReviewAgent : IAgent
{
    private readonly ILogger<ReviewAgent> _logger;

    public AgentType Type => AgentType.Review;
    public string Name => "Review Agent";
    public string Description => "Reviews generated code against requirements for correctness, security, compliance, and traceability.";

    public ReviewAgent(ILogger<ReviewAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("ReviewAgent starting — {ArtifactCount} artifacts, {ReqCount} requirements",
            context.Artifacts.Count, context.Requirements.Count);

        var findings = new List<ReviewFinding>();

        try
        {
            foreach (var artifact in context.Artifacts)
            {
                ct.ThrowIfCancellationRequested();
                findings.AddRange(ReviewArtifact(artifact, context.Requirements));
            }

            // Cross-cutting checks
            findings.AddRange(CheckRequirementCoverage(context));
            findings.AddRange(CheckSecurityPatterns(context));
            findings.AddRange(CheckMultiTenancy(context));

            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            var errorCount = findings.Count(f => f.Severity >= ReviewSeverity.Error);
            return new AgentResult
            {
                Agent = Type, Success = errorCount == 0,
                Summary = $"Review complete: {findings.Count} findings ({errorCount} errors, {findings.Count - errorCount} warnings/info)",
                Findings = findings,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator, Subject = "Review complete",
                    Body = $"{findings.Count} findings. {errorCount} blocking errors." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ReviewAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static List<ReviewFinding> ReviewArtifact(CodeArtifact artifact, List<Requirement> requirements)
    {
        var findings = new List<ReviewFinding>();

        // Check requirement traceability
        if (artifact.TracedRequirementIds.Count == 0)
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Warning,
                Category = "Traceability",
                Message = $"Artifact '{artifact.FileName}' has no traced requirements.",
                Suggestion = "Link this artifact to at least one requirement ID for audit traceability."
            });
        }

        // Check for tenant_id presence in DB artifacts
        if (artifact.Layer == ArtifactLayer.Database && !artifact.Content.Contains("TenantId", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.SecurityViolation,
                Category = "MultiTenant",
                Message = $"Entity '{artifact.FileName}' is missing TenantId property — multi-tenant isolation risk.",
                Suggestion = "Add [Required] public string TenantId to every regulated entity."
            });
        }

        // Check for audit columns in DB entities
        if (artifact.Layer == ArtifactLayer.Database &&
            !artifact.Content.Contains("CreatedAt", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.ComplianceViolation,
                Category = "Audit",
                Message = $"Entity '{artifact.FileName}' missing audit timestamp columns.",
                Suggestion = "Add CreatedAt, CreatedBy, UpdatedAt, UpdatedBy for HIPAA audit trail."
            });
        }

        // Check namespace conventions
        if (string.IsNullOrWhiteSpace(artifact.Namespace))
        {
            findings.Add(new ReviewFinding
            {
                ArtifactId = artifact.Id,
                FilePath = artifact.RelativePath,
                Severity = ReviewSeverity.Warning,
                Category = "Conventions",
                Message = $"Artifact '{artifact.FileName}' has no namespace defined.",
                Suggestion = "All C# files should have a proper namespace matching the project structure."
            });
        }

        return findings;
    }

    private static List<ReviewFinding> CheckRequirementCoverage(AgentContext context)
    {
        var findings = new List<ReviewFinding>();
        var coveredIds = context.Artifacts
            .SelectMany(a => a.TracedRequirementIds)
            .ToHashSet();

        var dbReqs = context.Requirements
            .Where(r => r.Tags.Exists(t => t is "Patient" or "Encounter" or "Inpatient" or "Emergency" or "Revenue" or "Diagnostics"))
            .ToList();

        var uncovered = dbReqs.Where(r => !coveredIds.Contains(r.Id)).ToList();

        if (uncovered.Count > 0)
        {
            findings.Add(new ReviewFinding
            {
                Severity = ReviewSeverity.Warning,
                Category = "Coverage",
                Message = $"{uncovered.Count} domain requirements have no traced artifacts.",
                Suggestion = $"Uncovered: {string.Join(", ", uncovered.Take(10).Select(r => r.Id))}"
            });
        }

        return findings;
    }

    private static List<ReviewFinding> CheckSecurityPatterns(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        var repoArtifacts = context.Artifacts.Where(a => a.Layer == ArtifactLayer.Repository).ToList();
        foreach (var repo in repoArtifacts)
        {
            if (!repo.Content.Contains("tenant", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = repo.Id,
                    FilePath = repo.RelativePath,
                    Severity = ReviewSeverity.SecurityViolation,
                    Category = "Security",
                    Message = $"Repository '{repo.FileName}' may not enforce tenant isolation in queries.",
                    Suggestion = "Ensure all queries filter by tenant_id via EF global query filter or explicit parameter."
                });
            }
        }

        return findings;
    }

    private static List<ReviewFinding> CheckMultiTenancy(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        var hasRls = context.Artifacts.Any(a =>
            a.FileName.Contains("Rls", StringComparison.OrdinalIgnoreCase) ||
            a.Content.Contains("ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));

        if (!hasRls)
        {
            findings.Add(new ReviewFinding
            {
                Severity = ReviewSeverity.Error,
                Category = "MultiTenant",
                Message = "No Row-Level Security policy artifact found.",
                Suggestion = "DatabaseAgent should generate PostgreSQL RLS policies for all regulated tables."
            });
        }

        var hasQueryFilter = context.Artifacts.Any(a =>
            a.Content.Contains("HasQueryFilter", StringComparison.OrdinalIgnoreCase));

        if (!hasQueryFilter)
        {
            findings.Add(new ReviewFinding
            {
                Severity = ReviewSeverity.Error,
                Category = "MultiTenant",
                Message = "No EF Core global query filter for tenant isolation found.",
                Suggestion = "DbContext.OnModelCreating should set HasQueryFilter(e => e.TenantId == _tenantId) on all entities."
            });
        }

        return findings;
    }
}
