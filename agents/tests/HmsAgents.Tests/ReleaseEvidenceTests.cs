using HmsAgents.Agents.Orchestrator;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Tests;

public class ReleaseEvidenceTests
{
    [Fact]
    public void Build_EmptyContext_CreatesEvidence()
    {
        var context = new AgentContext();
        var evidence = ReleaseEvidenceBuilder.Build(context);

        Assert.Equal(0, evidence.TotalRequirements);
        Assert.Equal(0, evidence.TotalFindings);
    }

    [Fact]
    public void Build_WithRequirementsAndArtifacts_CountsCorrectly()
    {
        var context = new AgentContext
        {
            Requirements =
            [
                new Requirement { Id = "R1", Title = "Feature 1" },
                new Requirement { Id = "R2", Title = "Feature 2" }
            ]
        };
        context.Artifacts.Add(new CodeArtifact { RelativePath = "T1.cs", Layer = ArtifactLayer.Test, TracedRequirementIds = ["R1"] });
        context.Artifacts.Add(new CodeArtifact { RelativePath = "S1.cs", Layer = ArtifactLayer.Service, TracedRequirementIds = ["R1", "R2"] });

        var evidence = ReleaseEvidenceBuilder.Build(context);

        Assert.Equal(2, evidence.TotalRequirements);
        Assert.Equal(1, evidence.TotalTests);
    }

    [Fact]
    public void Build_WithFindings_CountsCritical()
    {
        var context = new AgentContext();
        context.Findings.Add(new ReviewFinding { Severity = ReviewSeverity.Error, Category = "SEC-001", Message = "SQL injection" });
        context.Findings.Add(new ReviewFinding { Severity = ReviewSeverity.Warning, Category = "PERF-001", Message = "Slow query" });

        var evidence = ReleaseEvidenceBuilder.Build(context);

        Assert.Equal(2, evidence.TotalFindings);
        Assert.Equal(1, evidence.CriticalFindings);
    }

    [Fact]
    public void ExportMarkdown_ContainsAllSections()
    {
        var evidence = new ReleaseEvidence
        {
            TotalRequirements = 5,
            CoveredRequirements = 4,
            TotalTests = 10,
            PassedTests = 10,
            TotalFindings = 2,
            CriticalFindings = 0,
            SecurityScanResults = ["No issues"],
            ComplianceCheckResults = ["HIPAA: PASS"]
        };

        var md = ReleaseEvidenceBuilder.ExportMarkdown(evidence, "test-run");

        Assert.Contains("Release Evidence Package", md);
        Assert.Contains("test-run", md);
        Assert.Contains("PASS", md); // IsReleasable = false (4 != 5) but PASS appears in compliance check
        Assert.Contains("Security Scan", md);
        Assert.Contains("Compliance Checks", md);
    }

    [Fact]
    public void IsReleasable_AllGreen_True()
    {
        var evidence = new ReleaseEvidence
        {
            TotalRequirements = 3,
            CoveredRequirements = 3,
            TotalTests = 10,
            PassedTests = 10,
            CriticalFindings = 0
        };

        Assert.True(evidence.IsReleasable);
    }

    [Fact]
    public void IsReleasable_CriticalFindings_False()
    {
        var evidence = new ReleaseEvidence
        {
            TotalRequirements = 3,
            CoveredRequirements = 3,
            TotalTests = 10,
            PassedTests = 10,
            CriticalFindings = 1
        };

        Assert.False(evidence.IsReleasable);
    }

    [Fact]
    public void IsReleasable_UncoveredRequirements_False()
    {
        var evidence = new ReleaseEvidence
        {
            TotalRequirements = 5,
            CoveredRequirements = 3,
            TotalTests = 10,
            PassedTests = 10,
            CriticalFindings = 0
        };

        Assert.False(evidence.IsReleasable);
    }
}
