using System.Diagnostics;
using HmsAgents.Agents.Requirements;
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
    public string Description => "Reviews generated code against requirements for correctness, security, compliance, feature coverage, and NFR conformance.";

    public ReviewAgent(ILogger<ReviewAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        // Remove previous Review findings to prevent duplication on re-runs
        context.Findings.RemoveAll(f => f.Category is "Traceability" or "MultiTenant" or "Audit" or "Conventions"
            or "Coverage" or "Security" or "NFR-CODE-01" or "NFR-CODE-02" or "NFR-TEST-01"
            or "Implementation" or "FeatureCoverage");

        _logger.LogInformation("ReviewAgent starting — {ArtifactCount} artifacts, {ReqCount} requirements",
            context.Artifacts.Count, context.Requirements.Count);

        var findings = new List<ReviewFinding>();

        try
        {
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Reviewing {context.Artifacts.Count} artifacts against {context.Requirements.Count} requirements for correctness, security, compliance");
            foreach (var artifact in context.Artifacts)
            {
                ct.ThrowIfCancellationRequested();
                findings.AddRange(ReviewArtifact(artifact, context.Requirements));
            }

            // Cross-cutting checks
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Running cross-cutting checks — requirement coverage, security patterns, multi-tenancy enforcement");
            findings.AddRange(CheckRequirementCoverage(context));
            findings.AddRange(CheckSecurityPatterns(context));
            findings.AddRange(CheckMultiTenancy(context));

            // New NFR-driven checks
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Running NFR checks — TODO comments, DTO field coverage, test stubs, service completeness, feature mapping");
            findings.AddRange(CheckForTodoComments(context));
            findings.AddRange(CheckDtoFieldCoverage(context));
            findings.AddRange(CheckTestStubs(context));
            findings.AddRange(CheckServiceImplementationCompleteness(context));
            findings.AddRange(CheckFeatureMappingCoverage(context));

            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            var errorCount = findings.Count(f => f.Severity >= ReviewSeverity.Error);
            var secCount = findings.Count(f => f.Severity == ReviewSeverity.SecurityViolation);
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Review complete: {findings.Count} findings — {errorCount} errors, {secCount} security violations, {findings.Count - errorCount - secCount} warnings/info");
            _logger.LogInformation("Review done: {Total} findings, {Errors} errors, {Security} security",
                findings.Count, errorCount, secCount);

            return new AgentResult
            {
                Agent = Type, Success = true,  // Findings are dispatched via Phase 2, not pipeline-breaking
                Summary = $"Review complete: {findings.Count} findings ({errorCount} errors, {secCount} security violations, {findings.Count - errorCount - secCount} warnings/info)",
                Findings = findings,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator, Subject = "Review complete",
                    Body = $"{findings.Count} findings. {errorCount} blocking errors, {secCount} security violations. Iteration: {context.ReviewIteration}" }],
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

    // ─── NFR-CODE-01: No TODO comments in generated code ────────────────────

    private static List<ReviewFinding> CheckForTodoComments(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        foreach (var artifact in context.Artifacts.Where(a =>
            a.Layer is ArtifactLayer.Service or ArtifactLayer.Repository or ArtifactLayer.Dto))
        {
            var content = artifact.Content;
            var todoIdx = content.IndexOf("// TODO", StringComparison.OrdinalIgnoreCase);
            if (todoIdx >= 0)
            {
                var line = content[..todoIdx].Count(c => c == '\n') + 1;
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Error,
                    Category = "NFR-CODE-01",
                    Message = $"TODO comment found at line ~{line} in '{artifact.FileName}'. Generated code must be complete.",
                    Suggestion = "ServiceLayerAgent must generate complete implementation using ParsedDomainModel entity fields."
                });
            }
        }

        return findings;
    }

    // ─── NFR-CODE-02: DTO field coverage against entity fields ──────────────

    private static List<ReviewFinding> CheckDtoFieldCoverage(AgentContext context)
    {
        var findings = new List<ReviewFinding>();
        var model = context.DomainModel;
        if (model is null) return findings;

        foreach (var entity in model.Entities.Where(e => e.Fields.Count > 0))
        {
            var dtoArtifact = context.Artifacts.FirstOrDefault(a =>
                a.Layer == ArtifactLayer.Dto &&
                a.FileName == $"{entity.Name}Dto.cs");

            if (dtoArtifact is null)
            {
                findings.Add(new ReviewFinding
                {
                    Severity = ReviewSeverity.Error,
                    Category = "NFR-CODE-02",
                    Message = $"No DTO artifact found for entity '{entity.Name}'.",
                    Suggestion = $"ServiceLayerAgent must generate {entity.Name}Dto.cs"
                });
                continue;
            }

            // Check that non-navigation fields are present in the DTO
            var nonNavFields = entity.Fields.Where(f => !f.IsNavigation).ToList();
            var missingFields = nonNavFields
                .Where(f => !dtoArtifact.Content.Contains(f.Name))
                .Select(f => f.Name)
                .ToList();

            if (missingFields.Count > 0)
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = dtoArtifact.Id,
                    FilePath = dtoArtifact.RelativePath,
                    Severity = missingFields.Count > nonNavFields.Count / 2
                        ? ReviewSeverity.Error : ReviewSeverity.Warning,
                    Category = "NFR-CODE-02",
                    Message = $"DTO '{entity.Name}Dto' is missing {missingFields.Count}/{nonNavFields.Count} fields: {string.Join(", ", missingFields.Take(5))}",
                    Suggestion = "DTO must map all entity fields. Use ParsedDomainModel fields to generate complete DTOs."
                });
            }
        }

        return findings;
    }

    // ─── NFR-TEST-01: No stub tests ─────────────────────────────────────────

    private static List<ReviewFinding> CheckTestStubs(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        foreach (var artifact in context.Artifacts.Where(a => a.Layer == ArtifactLayer.Test))
        {
            if (artifact.Content.Contains("Assert.True(true,", StringComparison.Ordinal))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Error,
                    Category = "NFR-TEST-01",
                    Message = $"Stub assertion 'Assert.True(true, ...)' found in '{artifact.FileName}'. Tests must have real logic.",
                    Suggestion = "TestingAgent must generate Moq-based tests with real assertions against service behavior."
                });
            }

            if (artifact.Content.Contains("// TODO", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Error,
                    Category = "NFR-TEST-01",
                    Message = $"TODO found in test '{artifact.FileName}'. Tests must be fully implemented.",
                    Suggestion = "Remove TODO comments and implement the test logic."
                });
            }
        }

        return findings;
    }

    // ─── Service implementation completeness ────────────────────────────────

    private static List<ReviewFinding> CheckServiceImplementationCompleteness(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        foreach (var artifact in context.Artifacts.Where(a =>
            a.Layer == ArtifactLayer.Service && !a.FileName.StartsWith("I")))
        {
            // Check CreateAsync actually calls repository
            if (artifact.Content.Contains("CreateAsync") &&
                !artifact.Content.Contains("_repo.CreateAsync"))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Error,
                    Category = "Implementation",
                    Message = $"Service '{artifact.FileName}' CreateAsync does not persist via repository.",
                    Suggestion = "CreateAsync must call _repo.CreateAsync(entity, ct) and return mapped DTO."
                });
            }

            // Check UpdateAsync loads entity before modifying
            if (artifact.Content.Contains("UpdateAsync") &&
                !artifact.Content.Contains("_repo.GetByIdAsync"))
            {
                findings.Add(new ReviewFinding
                {
                    ArtifactId = artifact.Id,
                    FilePath = artifact.RelativePath,
                    Severity = ReviewSeverity.Error,
                    Category = "Implementation",
                    Message = $"Service '{artifact.FileName}' UpdateAsync does not load entity from repository before modifying.",
                    Suggestion = "UpdateAsync must load entity via _repo.GetByIdAsync, apply changes, then save."
                });
            }
        }

        return findings;
    }

    // ─── Feature mapping coverage ───────────────────────────────────────────

    private static List<ReviewFinding> CheckFeatureMappingCoverage(AgentContext context)
    {
        var findings = new List<ReviewFinding>();
        var model = context.DomainModel;
        if (model is null) return findings;

        var allTracedFeatures = context.Artifacts
            .SelectMany(a => a.TracedRequirementIds)
            .ToHashSet();

        foreach (var feature in model.FeatureMappings)
        {
            if (!allTracedFeatures.Contains(feature.FeatureId))
            {
                findings.Add(new ReviewFinding
                {
                    Severity = ReviewSeverity.Warning,
                    Category = "FeatureCoverage",
                    Message = $"Feature '{feature.FeatureId}' ({feature.FeatureName}) has no traced artifacts.",
                    Suggestion = $"Services: {string.Join(", ", feature.ServiceNames)}; Entities: {string.Join(", ", feature.EntityNames)}"
                });
            }

            // Check that test artifacts also trace the feature
            var testArtifacts = context.Artifacts.Where(a =>
                a.Layer == ArtifactLayer.Test &&
                a.TracedRequirementIds.Contains(feature.FeatureId)).ToList();

            if (testArtifacts.Count == 0)
            {
                findings.Add(new ReviewFinding
                {
                    Severity = ReviewSeverity.Warning,
                    Category = "TestCoverage",
                    Message = $"Feature '{feature.FeatureId}' ({feature.FeatureName}) has no traced test artifacts.",
                    Suggestion = "TestingAgent should generate tests tagged with this feature ID."
                });
            }
        }

        return findings;
    }
}
