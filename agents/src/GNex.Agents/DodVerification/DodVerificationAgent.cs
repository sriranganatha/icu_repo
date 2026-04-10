using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.DodVerification;

/// <summary>
/// Verifies Definition of Done (DOD) for completed work items.
/// Inspects each DOD checklist entry against generated artifacts.
/// - If all DOD items pass → marks as verified (DodVerified = true)
/// - If any DOD item fails → reopens the item (InQueue) with verification notes
///
/// This agent runs AFTER code-gen agents complete items, acting as a quality gate.
/// It does NOT claim items via lifecycle — it scans completed items directly.
/// </summary>
public sealed class DodVerificationAgent : IAgent
{
    private readonly ILogger<DodVerificationAgent> _logger;

    public AgentType Type => AgentType.DodVerification;
    public string Name => "DOD Verification Agent";
    public string Description => "Verifies Definition of Done for completed work items against generated artifacts.";

    public DodVerificationAgent(ILogger<DodVerificationAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        var completedItems = context.ExpandedRequirements
            .Where(i => i.Status == WorkItemStatus.Completed
                     && !i.DodVerified
                     && i.DefinitionOfDone.Count > 0
                     && i.ItemType is WorkItemType.Task or WorkItemType.Bug)
            .ToList();

        _logger.LogInformation("DodVerificationAgent starting — {Count} completed items to verify", completedItems.Count);
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Verifying DOD for {completedItems.Count} completed items");

        var verifiedCount = 0;
        var reopenedCount = 0;
        var artifacts = context.Artifacts.ToList();

        foreach (var item in completedItems)
        {
            ct.ThrowIfCancellationRequested();

            var dodResults = new Dictionary<string, bool>();
            var notes = new List<string>();
            var allPassed = true;

            foreach (var dodEntry in item.DefinitionOfDone)
            {
                var passed = VerifyDodEntry(dodEntry, item, artifacts, out var reason);
                dodResults[dodEntry] = passed;

                if (!passed)
                {
                    allPassed = false;
                    notes.Add($"DOD FAIL: \"{dodEntry}\" — {reason}");
                    _logger.LogWarning("DOD failed for {ItemId}: {Dod} — {Reason}", item.Id, dodEntry, reason);
                }
            }

            item.DodVerificationStatus = dodResults;

            if (allPassed)
            {
                item.DodVerified = true;
                item.DodVerificationNotes = ["All DOD items verified"];
                verifiedCount++;
                _logger.LogInformation("DOD verified for {ItemId}: {Title}", item.Id, item.Title);

                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"VERIFIED: {item.Title} — all {item.DefinitionOfDone.Count} DOD items passed");
            }
            else
            {
                // Reopen the item so agents process it again
                item.DodVerified = false;
                item.DodVerificationNotes = notes;
                item.Status = WorkItemStatus.InQueue;
                item.CompletedAt = null;
                item.AssignedAgent = string.Empty;
                reopenedCount++;

                _logger.LogWarning("DOD failed for {ItemId}: {Title} — {Failed}/{Total} items failed, reopening",
                    item.Id, item.Title, notes.Count, item.DefinitionOfDone.Count);

                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"REOPENED: {item.Title} — {notes.Count}/{item.DefinitionOfDone.Count} DOD items failed");

                // Add findings so Review/BugFix agents can see the DOD gaps
                foreach (var note in notes)
                {
                    context.Findings.Add(new ReviewFinding
                    {
                        Id = $"DOD-{item.Id}-{Guid.NewGuid():N}"[..24],
                        Category = "DodVerification",
                        Severity = ReviewSeverity.Warning,
                        Message = $"[{item.Id}] {note}",
                        Suggestion = $"Ensure '{item.Title}' DOD criteria are met by generated artifacts",
                        ArtifactId = string.Empty
                    });
                }
            }
        }

        context.AgentStatuses[Type] = AgentStatus.Completed;

        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"DOD Verification: {verifiedCount} verified, {reopenedCount} reopened out of {completedItems.Count} items",
            Messages =
            [
                new AgentMessage
                {
                    From = Type,
                    To = AgentType.Orchestrator,
                    Subject = "DOD verification complete",
                    Body = $"Verified: {verifiedCount}, Reopened: {reopenedCount}. " +
                           $"Items with DOD: {completedItems.Count}"
                }
            ],
            Duration = sw.Elapsed
        };
    }

    /// <summary>
    /// Verify a single DOD entry against the artifact set.
    /// Returns true if the DOD criterion is satisfied, false with reason if not.
    /// </summary>
    private static bool VerifyDodEntry(string dod, ExpandedRequirement item, List<CodeArtifact> artifacts, out string reason)
    {
        var dodLower = dod.ToLowerInvariant();

        // Find artifacts that trace to this requirement
        var tracedArtifacts = artifacts
            .Where(a => a.TracedRequirementIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase)
                     || a.TracedRequirementIds.Contains(item.SourceRequirementId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Also find artifacts matching by service/module name
        var module = item.Module?.ToLowerInvariant() ?? string.Empty;
        var relatedArtifacts = artifacts
            .Where(a => !string.IsNullOrEmpty(module) &&
                        (a.RelativePath.Contains(module, StringComparison.OrdinalIgnoreCase) ||
                         a.Namespace?.Contains(module, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        var allRelevant = tracedArtifacts.Concat(relatedArtifacts).DistinctBy(a => a.Id).ToList();

        // ── Pattern: "unit test" / "test" DOD items ──
        if (dodLower.Contains("test"))
        {
            var testArtifacts = allRelevant.Where(a => a.Layer == ArtifactLayer.Test).ToList();
            if (testArtifacts.Count == 0)
            {
                // Broaden: check any test artifact
                testArtifacts = artifacts.Where(a =>
                    a.Layer == ArtifactLayer.Test &&
                    !string.IsNullOrEmpty(module) &&
                    a.RelativePath.Contains(module, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (testArtifacts.Count == 0)
            {
                reason = "No test artifacts found for this item";
                return false;
            }

            // Check tests contain actual assertions, not stubs
            var hasRealAssertions = testArtifacts.Any(a =>
                a.Content.Contains("Assert.", StringComparison.OrdinalIgnoreCase) ||
                a.Content.Contains(".Should()", StringComparison.OrdinalIgnoreCase) ||
                a.Content.Contains("Verify(", StringComparison.OrdinalIgnoreCase));
            if (!hasRealAssertions)
            {
                reason = "Test artifacts exist but contain no real assertions";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "database" / "schema" / "migration" / "table" DOD items ──
        if (dodLower.Contains("database") || dodLower.Contains("schema") || dodLower.Contains("migration") ||
            dodLower.Contains("table") || dodLower.Contains("entity"))
        {
            var dbArtifacts = allRelevant.Where(a => a.Layer == ArtifactLayer.Database).ToList();
            if (dbArtifacts.Count == 0)
            {
                dbArtifacts = artifacts.Where(a =>
                    a.Layer == ArtifactLayer.Database &&
                    !string.IsNullOrEmpty(module) &&
                    a.RelativePath.Contains(module, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (dbArtifacts.Count == 0)
            {
                reason = "No database artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "api" / "endpoint" DOD items ──
        if (dodLower.Contains("api") || dodLower.Contains("endpoint") || dodLower.Contains("route"))
        {
            var apiArtifacts = allRelevant.Where(a => a.Layer is ArtifactLayer.Service or ArtifactLayer.RazorPage).ToList();
            if (apiArtifacts.Count == 0)
            {
                apiArtifacts = artifacts.Where(a =>
                    a.Layer is ArtifactLayer.Service or ArtifactLayer.RazorPage &&
                    !string.IsNullOrEmpty(module) &&
                    a.RelativePath.Contains(module, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (apiArtifacts.Count == 0)
            {
                reason = "No API/endpoint artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "service" / "business logic" / "dto" DOD items ──
        if (dodLower.Contains("service") || dodLower.Contains("business logic") || dodLower.Contains("dto") ||
            dodLower.Contains("validation"))
        {
            var svcArtifacts = allRelevant.Where(a => a.Layer == ArtifactLayer.Service).ToList();
            if (svcArtifacts.Count == 0)
            {
                svcArtifacts = artifacts.Where(a =>
                    a.Layer == ArtifactLayer.Service &&
                    !string.IsNullOrEmpty(module) &&
                    a.RelativePath.Contains(module, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (svcArtifacts.Count == 0)
            {
                reason = "No service-layer artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "integration" / "kafka" / "fhir" / "hl7" DOD items ──
        if (dodLower.Contains("integration") || dodLower.Contains("kafka") || dodLower.Contains("fhir") ||
            dodLower.Contains("hl7") || dodLower.Contains("event"))
        {
            var intArtifacts = allRelevant.Where(a => a.Layer == ArtifactLayer.Integration).ToList();
            if (intArtifacts.Count == 0)
            {
                intArtifacts = artifacts.Where(a =>
                    a.Layer == ArtifactLayer.Integration &&
                    !string.IsNullOrEmpty(module) &&
                    a.RelativePath.Contains(module, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (intArtifacts.Count == 0)
            {
                reason = "No integration artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "security" / "hipaa" / "compliance" DOD items ──
        if (dodLower.Contains("security") || dodLower.Contains("hipaa") || dodLower.Contains("compliance") ||
            dodLower.Contains("rbac") || dodLower.Contains("access control"))
        {
            var secArtifacts = allRelevant.Where(a =>
                a.Layer == ArtifactLayer.Security ||
                a.ProducedBy is AgentType.Security or AgentType.HipaaCompliance or AgentType.Soc2Compliance or AgentType.AccessControl).ToList();
            if (secArtifacts.Count == 0)
            {
                reason = "No security/compliance artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "documentation" / "swagger" DOD items ──
        if (dodLower.Contains("document") || dodLower.Contains("swagger") || dodLower.Contains("openapi"))
        {
            var docArtifacts = allRelevant.Where(a =>
                a.Layer == ArtifactLayer.Documentation ||
                a.ProducedBy == AgentType.ApiDocumentation).ToList();
            if (docArtifacts.Count == 0)
            {
                reason = "No documentation artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: "observability" / "logging" / "monitoring" DOD items ──
        if (dodLower.Contains("observability") || dodLower.Contains("logging") || dodLower.Contains("monitoring") ||
            dodLower.Contains("metrics") || dodLower.Contains("health check"))
        {
            var obsArtifacts = allRelevant.Where(a =>
                a.ProducedBy is AgentType.Observability or AgentType.Monitor).ToList();
            if (obsArtifacts.Count == 0)
            {
                reason = "No observability artifacts found for this item";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Pattern: content-based check for "tenant" / "audit" in artifact code ──
        if (dodLower.Contains("tenant"))
        {
            var hasTenantId = allRelevant.Any(a =>
                a.Content.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
            if (!hasTenantId)
            {
                reason = "No artifacts contain TenantId property for multi-tenant support";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        if (dodLower.Contains("audit"))
        {
            var hasAudit = allRelevant.Any(a =>
                a.Content.Contains("CreatedAt", StringComparison.OrdinalIgnoreCase) &&
                a.Content.Contains("UpdatedAt", StringComparison.OrdinalIgnoreCase));
            if (!hasAudit)
            {
                reason = "No artifacts contain audit columns (CreatedAt/UpdatedAt)";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ── Default: if we have ANY relevant artifacts, consider the DOD item met ──
        if (allRelevant.Count > 0)
        {
            reason = string.Empty;
            return true;
        }

        // No artifacts found at all for this item
        reason = "No artifacts found that match this work item";
        return false;
    }
}
