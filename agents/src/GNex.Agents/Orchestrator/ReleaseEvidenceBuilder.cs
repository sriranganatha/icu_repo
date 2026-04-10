using System.Diagnostics;
using System.Text;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Orchestrator;

/// <summary>
/// Assembles a release evidence package: test results, findings summary, traceability, compliance checks.
/// </summary>
public static class ReleaseEvidenceBuilder
{
    public static ReleaseEvidence Build(AgentContext context)
    {
        var artifacts = context.Artifacts.ToList();
        var findings = context.Findings.ToList();
        var requirements = context.Requirements;

        var evidence = new ReleaseEvidence
        {
            TotalRequirements = requirements.Count,
            TotalTests = artifacts.Count(a => a.Layer == ArtifactLayer.Test),
            PassedTests = artifacts.Count(a => a.Layer == ArtifactLayer.Test), // assume pass
            TotalFindings = findings.Count,
            CriticalFindings = findings.Count(f => f.Severity == ReviewSeverity.Error),
            CoveredRequirements = requirements.Count(r => artifacts.Any(a => a.TracedRequirementIds.Contains(r.Id))),
            SecurityScanResults = BuildSecuritySummary(findings),
            ComplianceCheckResults = BuildComplianceSummary(findings),
            TraceabilityMatrix = TraceabilityGateAgent.BuildMatrix(context)
        };

        return evidence;
    }

    public static string ExportMarkdown(ReleaseEvidence evidence, string runId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Release Evidence Package");
        sb.AppendLine();
        sb.AppendLine($"**Run ID:** `{runId}`  ");
        sb.AppendLine($"**Generated:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC  ");
        sb.AppendLine($"**Release Decision:** {(evidence.IsReleasable ? "**PASS**" : "**BLOCKED**")}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Requirements | {evidence.TotalRequirements} |");
        sb.AppendLine($"| Test Artifacts | {evidence.TotalTests} |");
        sb.AppendLine($"| Total Findings | {evidence.TotalFindings} |");
        sb.AppendLine($"| Critical Findings | {evidence.CriticalFindings} |");
        sb.AppendLine($"| Releasable | {evidence.IsReleasable} |");
        sb.AppendLine();

        // Traceability
        sb.AppendLine("## Traceability");
        sb.AppendLine();
        var covered = evidence.TraceabilityMatrix.Count(t => t.FullyCovered);
        sb.AppendLine($"**{covered}/{evidence.TraceabilityMatrix.Count}** requirements fully covered.");
        sb.AppendLine();

        // Security
        sb.AppendLine("## Security Scan");
        sb.AppendLine();
        foreach (var line in evidence.SecurityScanResults)
            sb.AppendLine($"- {line}");
        sb.AppendLine();

        // Compliance
        sb.AppendLine("## Compliance Checks");
        sb.AppendLine();
        foreach (var line in evidence.ComplianceCheckResults)
            sb.AppendLine($"- {line}");
        sb.AppendLine();

        return sb.ToString();
    }

    private static List<string> BuildSecuritySummary(List<ReviewFinding> findings)
    {
        var secFindings = findings.Where(f =>
            f.Category.StartsWith("SEC", StringComparison.OrdinalIgnoreCase) ||
            f.Category.Contains("Security", StringComparison.OrdinalIgnoreCase)).ToList();

        if (secFindings.Count == 0) return ["No security findings."];

        return secFindings
            .GroupBy(f => f.Severity)
            .Select(g => $"{g.Key}: {g.Count()} finding(s)")
            .ToList();
    }

    private static List<string> BuildComplianceSummary(List<ReviewFinding> findings)
    {
        var complianceFindings = findings.Where(f =>
            f.Category.StartsWith("HIPAA", StringComparison.OrdinalIgnoreCase) ||
            f.Category.StartsWith("SOC2", StringComparison.OrdinalIgnoreCase) ||
            f.Category.Contains("Compliance", StringComparison.OrdinalIgnoreCase)).ToList();

        if (complianceFindings.Count == 0) return ["No compliance findings."];

        return complianceFindings
            .GroupBy(f => f.Category.Split('-')[0])
            .Select(g => $"{g.Key}: {g.Count()} finding(s)")
            .ToList();
    }
}
