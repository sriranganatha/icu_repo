using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Tests the full artifact lifecycle: creation, deduplication, project scoping,
/// traceability linkage, and content integrity.
/// </summary>
public class ArtifactLifecycleTests
{
    // ── Creation & defaults ──

    [Fact]
    public void CodeArtifact_HasUniqueId()
    {
        var a1 = new CodeArtifact();
        var a2 = new CodeArtifact();
        a1.Id.Should().NotBe(a2.Id);
    }

    [Fact]
    public void CodeArtifact_DefaultTimestamp_IsRecentUtc()
    {
        var a = new CodeArtifact();
        a.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CodeArtifact_TracedRequirementIds_DefaultsEmpty()
    {
        var a = new CodeArtifact();
        a.TracedRequirementIds.Should().BeEmpty();
    }

    [Fact]
    public void CodeArtifact_Content_StoredAndRetrieved()
    {
        var content = "public class Patient { public Guid Id { get; set; } }";
        var a = new CodeArtifact { Content = content };
        a.Content.Should().Be(content);
    }

    [Fact]
    public void CodeArtifact_LargeContent_Preserved()
    {
        var content = new string('x', 1_000_000); // 1MB of data
        var a = new CodeArtifact { Content = content };
        a.Content.Length.Should().Be(1_000_000);
    }

    // ── Project scoping ──

    [Fact]
    public void StampProjectScope_SetsProjectIdOnUnscoped()
    {
        var ctx = new AgentContext { ProjectId = "proj-1" };
        ctx.Artifacts.Add(new CodeArtifact { FileName = "a.cs" });
        ctx.Artifacts.Add(new CodeArtifact { FileName = "b.cs", ProjectId = "proj-2" });

        ctx.StampProjectScope();

        var artifacts = ctx.Artifacts.ToList();
        artifacts.Single(a => a.FileName == "a.cs").ProjectId.Should().Be("proj-1");
        artifacts.Single(a => a.FileName == "b.cs").ProjectId.Should().Be("proj-2"); // preserved
    }

    [Fact]
    public void StampProjectScope_NullProjectId_DoesNothing()
    {
        var ctx = new AgentContext { ProjectId = null };
        ctx.Artifacts.Add(new CodeArtifact { FileName = "a.cs" });

        ctx.StampProjectScope();

        ctx.Artifacts.First().ProjectId.Should().BeNull();
    }

    [Fact]
    public void StampProjectScope_StampsAllCollections()
    {
        var ctx = new AgentContext { ProjectId = "proj-x" };
        ctx.Artifacts.Add(new CodeArtifact());
        ctx.Findings.Add(new ReviewFinding());
        ctx.TestDiagnostics.Add(new TestDiagnostic());
        ctx.Requirements.Add(new Requirement { Id = "R1" });
        ctx.ExpandedRequirements.Add(new ExpandedRequirement { Id = "ER1" });

        ctx.StampProjectScope();

        ctx.Artifacts.First().ProjectId.Should().Be("proj-x");
        ctx.Findings.First().ProjectId.Should().Be("proj-x");
        ctx.TestDiagnostics.First().ProjectId.Should().Be("proj-x");
        ctx.Requirements.First().ProjectId.Should().Be("proj-x");
        ctx.ExpandedRequirements.First().ProjectId.Should().Be("proj-x");
    }

    // ── Deduplication ──

    [Fact]
    public void DeduplicateArtifacts_RemovesDuplicatesByPath()
    {
        var ctx = new AgentContext();
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Services/PatientService.cs",
            Content = "v1", ProducedBy = AgentType.ServiceLayer
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Services/PatientService.cs",
            Content = "v2", ProducedBy = AgentType.ServiceLayer
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Services/ClaimService.cs",
            Content = "v1", ProducedBy = AgentType.ServiceLayer
        });

        ctx.DeduplicateArtifacts();

        ctx.Artifacts.Should().HaveCount(2);
        ctx.Artifacts.Select(a => a.RelativePath).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicateArtifacts_CaseInsensitive()
    {
        var ctx = new AgentContext();
        ctx.Artifacts.Add(new CodeArtifact { RelativePath = "db/Patient.cs", Content = "v1" });
        ctx.Artifacts.Add(new CodeArtifact { RelativePath = "DB/PATIENT.CS", Content = "v2" });

        ctx.DeduplicateArtifacts();

        ctx.Artifacts.Should().HaveCount(1);
    }

    [Fact]
    public void DeduplicateArtifacts_UsesIdForEmptyPath()
    {
        var ctx = new AgentContext();
        var a1 = new CodeArtifact { RelativePath = "", Content = "a" };
        var a2 = new CodeArtifact { RelativePath = "", Content = "b" };

        ctx.Artifacts.Add(a1);
        ctx.Artifacts.Add(a2);

        ctx.DeduplicateArtifacts();

        // Both have unique IDs → should keep both
        ctx.Artifacts.Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicateArtifacts_EmptyBag_NoError()
    {
        var ctx = new AgentContext();
        ctx.DeduplicateArtifacts();
        ctx.Artifacts.Should().BeEmpty();
    }

    // ── Finding deduplication ──

    [Fact]
    public void DeduplicateFindings_RemovesByFingerprint()
    {
        var ctx = new AgentContext();
        ctx.Findings.Add(new ReviewFinding
        {
            Category = "Security", FilePath = "Auth.cs", Message = "Missing RLS"
        });
        ctx.Findings.Add(new ReviewFinding
        {
            Category = "Security", FilePath = "Auth.cs", Message = "Missing RLS"
        });
        ctx.Findings.Add(new ReviewFinding
        {
            Category = "Compliance", FilePath = "Auth.cs", Message = "Missing audit"
        });

        ctx.DeduplicateFindings();

        ctx.Findings.Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicateFindings_CaseInsensitive()
    {
        var ctx = new AgentContext();
        ctx.Findings.Add(new ReviewFinding
        {
            Category = "SECURITY", FilePath = "auth.cs", Message = "Missing RLS"
        });
        ctx.Findings.Add(new ReviewFinding
        {
            Category = "security", FilePath = "AUTH.CS", Message = "missing rls"
        });

        ctx.DeduplicateFindings();

        ctx.Findings.Should().HaveCount(1);
    }

    // ── Traceability linkage ──

    [Fact]
    public void Artifact_WithTracedRequirements_MaintainsLinkage()
    {
        var req = new Requirement { Id = "REQ-001", Title = "Patient CRUD" };
        var artifact = new CodeArtifact
        {
            FileName = "PatientService.cs",
            TracedRequirementIds = ["REQ-001", "REQ-002"],
            ProducedBy = AgentType.ServiceLayer
        };

        artifact.TracedRequirementIds.Should().Contain("REQ-001");
        artifact.TracedRequirementIds.Should().HaveCount(2);
    }

    // ── Artifact layers ──

    [Theory]
    [InlineData(ArtifactLayer.Database)]
    [InlineData(ArtifactLayer.Service)]
    [InlineData(ArtifactLayer.Test)]
    [InlineData(ArtifactLayer.Integration)]
    [InlineData(ArtifactLayer.Infrastructure)]
    [InlineData(ArtifactLayer.Security)]
    [InlineData(ArtifactLayer.Compliance)]
    [InlineData(ArtifactLayer.Documentation)]
    public void Artifact_AllLayersAssignable(ArtifactLayer layer)
    {
        var a = new CodeArtifact { Layer = layer };
        a.Layer.Should().Be(layer);
    }

    // ── Multi-agent artifact accumulation ──

    [Fact]
    public void Context_AccumulatesArtifactsFromMultipleAgents()
    {
        var ctx = new AgentContext();

        // Simulate DB agent producing artifacts
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Database/Patient.cs", ProducedBy = AgentType.Database
        });
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Database/Encounter.cs", ProducedBy = AgentType.Database
        });

        // Simulate ServiceLayer agent
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Services/PatientService.cs", ProducedBy = AgentType.ServiceLayer
        });

        // Simulate Testing agent
        ctx.Artifacts.Add(new CodeArtifact
        {
            RelativePath = "Tests/PatientTests.cs", ProducedBy = AgentType.Testing
        });

        ctx.Artifacts.Should().HaveCount(4);
        ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Database).Should().HaveCount(2);
        ctx.Artifacts.Where(a => a.ProducedBy == AgentType.ServiceLayer).Should().HaveCount(1);
        ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Testing).Should().HaveCount(1);
    }
}
