using FluentAssertions;
using GNex.Agents.DodVerification;
using GNex.Core.Enums;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests;

/// <summary>
/// Tests the DodVerificationAgent:
///   1. Verifies DOD items against generated artifacts
///   2. Marks items as DodVerified when all criteria are met
///   3. Reopens items (InQueue) when DOD criteria fail
///   4. Creates findings for failed DOD items
/// </summary>
public class DodVerificationTests
{
    private readonly DodVerificationAgent _agent = new(new Mock<ILogger<DodVerificationAgent>>().Object);

    // ── All DOD items pass → verified ──

    [Fact]
    public async Task AllDodPass_MarksItemVerified()
    {
        var item = MakeCompletedItem("TASK-001", "Patient module",
        [
            "Unit tests written and passing",
            "Database schema created"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Test, "PatientServiceTests.cs", "Patient",
                "Assert.Equal(expected, actual);", tracedId: "TASK-001"),
            MakeArtifact(ArtifactLayer.Database, "PatientProfile.cs", "Patient",
                "public class PatientProfile { }", tracedId: "TASK-001")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeTrue();
        item.Status.Should().Be(WorkItemStatus.Completed, "verified items stay completed");
        item.DodVerificationStatus.Should().AllSatisfy(kv => kv.Value.Should().BeTrue());
    }

    // ── DOD item fails → reopened ──

    [Fact]
    public async Task DodFails_ReopensItem()
    {
        var item = MakeCompletedItem("TASK-002", "Encounter module",
        [
            "Unit tests written and passing",
            "Database schema created"
        ]);

        // Only DB artifact exists, no test artifact → test DOD fails
        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Database, "Encounter.cs", "Encounter",
                "public class Encounter { }", tracedId: "TASK-002")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeFalse();
        item.Status.Should().Be(WorkItemStatus.InQueue, "failed DOD should reopen the item");
        item.DodVerificationNotes.Should().Contain(n => n.Contains("No test artifacts"));
    }

    // ── Stub tests (no real assertions) fail DOD ──

    [Fact]
    public async Task StubTestsWithNoAssertions_FailDod()
    {
        var item = MakeCompletedItem("TASK-003", "Patient module",
        [
            "Unit tests written and passing"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Test, "PatientTests.cs", "Patient",
                "public void Test1() { /* TODO: implement */ }", tracedId: "TASK-003")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeFalse();
        item.DodVerificationNotes.Should().Contain(n => n.Contains("no real assertions"));
    }

    // ── Test with real assertions pass DOD ──

    [Fact]
    public async Task RealTestAssertions_PassDod()
    {
        var item = MakeCompletedItem("TASK-004", "Patient module",
        [
            "Unit tests written and passing"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Test, "PatientTests.cs", "Patient",
                "[Fact] public void GetById_ReturnsPatient() { var result = svc.Get(1); Assert.NotNull(result); }",
                tracedId: "TASK-004")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeTrue();
    }

    // ── API DOD ──

    [Fact]
    public async Task ApiDod_PassesWithServiceArtifact()
    {
        var item = MakeCompletedItem("TASK-005", "Patient module",
        [
            "API endpoint returns correct status codes"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Service, "PatientEndpoints.cs", "Patient",
                "app.MapGet(\"/patients/{id}\", ...)", tracedId: "TASK-005")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeTrue();
    }

    // ── Tenant DOD ──

    [Fact]
    public async Task TenantDod_FailsWithoutTenantId()
    {
        var item = MakeCompletedItem("TASK-006", "Patient module",
        [
            "Multi-tenant isolation verified"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Database, "Patient.cs", "Patient",
                "public class Patient { public Guid Id {get;set;} }", tracedId: "TASK-006")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeFalse();
        item.DodVerificationNotes.Should().Contain(n => n.Contains("TenantId"));
    }

    [Fact]
    public async Task TenantDod_PassesWithTenantId()
    {
        var item = MakeCompletedItem("TASK-007", "Patient module",
        [
            "Multi-tenant isolation verified"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Database, "Patient.cs", "Patient",
                "public class Patient { public Guid Id {get;set;} public string TenantId {get;set;} }",
                tracedId: "TASK-007")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeTrue();
    }

    // ── Audit DOD ──

    [Fact]
    public async Task AuditDod_FailsWithoutAuditColumns()
    {
        var item = MakeCompletedItem("TASK-008", "Patient module",
        [
            "Audit trail columns present"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Database, "Patient.cs", "Patient",
                "public class Patient { public Guid Id {get;set;} }", tracedId: "TASK-008")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeFalse();
        item.DodVerificationNotes.Should().Contain(n => n.Contains("audit columns"));
    }

    // ── DOD verification creates findings ──

    [Fact]
    public async Task FailedDod_CreatesFindingsForRemediation()
    {
        var item = MakeCompletedItem("TASK-009", "Patient module",
        [
            "Unit tests written and passing"
        ]);

        var context = CreateContext(item, []);  // No artifacts at all

        await _agent.ExecuteAsync(context);

        context.Findings.Should().Contain(f =>
            f.Category == "DodVerification" &&
            f.Severity == ReviewSeverity.Warning);
    }

    // ── Items without DOD are skipped ──

    [Fact]
    public async Task ItemsWithoutDod_AreSkipped()
    {
        var item = new ExpandedRequirement
        {
            Id = "TASK-010",
            ItemType = WorkItemType.Task,
            Status = WorkItemStatus.Completed,
            Title = "Simple task",
            DefinitionOfDone = []
        };

        var context = CreateContext(item, []);

        var result = await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeFalse("items without DOD should not be marked as verified");
        item.Status.Should().Be(WorkItemStatus.Completed, "items without DOD should not be reopened");
    }

    // ── Already verified items are skipped ──

    [Fact]
    public async Task AlreadyVerifiedItems_AreSkipped()
    {
        var item = MakeCompletedItem("TASK-011", "Patient module",
        [
            "Database schema created"
        ]);
        item.DodVerified = true;

        var context = CreateContext(item, []);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeTrue("already verified should remain verified");
        item.Status.Should().Be(WorkItemStatus.Completed);
    }

    // ── Result summary ──

    [Fact]
    public async Task Result_ReportsVerifiedAndReopenedCounts()
    {
        var item1 = MakeCompletedItem("TASK-A", "Module A", ["Database schema created"]);
        var item2 = MakeCompletedItem("TASK-B", "Module B", ["Unit tests written"]);

        var context = new AgentContext();
        context.ExpandedRequirements.Add(item1);
        context.ExpandedRequirements.Add(item2);

        context.Artifacts.Add(MakeArtifact(ArtifactLayer.Database, "A.cs", "Module A",
            "public class A { }", tracedId: "TASK-A"));
        // No test artifact for TASK-B

        var result = await _agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("1 verified");
        result.Summary.Should().Contain("1 reopened");
    }

    // ── Integration DOD passes with Kafka artifact ──

    [Fact]
    public async Task IntegrationDod_PassesWithKafkaArtifact()
    {
        var item = MakeCompletedItem("TASK-012", "Encounter module",
        [
            "Kafka integration events configured"
        ]);

        var context = CreateContext(item,
        [
            MakeArtifact(ArtifactLayer.Integration, "KafkaConsumer.cs", "Encounter",
                "public class EncounterKafkaConsumer { }", tracedId: "TASK-012")
        ]);

        await _agent.ExecuteAsync(context);

        item.DodVerified.Should().BeTrue();
    }

    // ── Helpers ──

    private static ExpandedRequirement MakeCompletedItem(string id, string module, List<string> dod) => new()
    {
        Id = id,
        ItemType = WorkItemType.Task,
        Status = WorkItemStatus.Completed,
        Title = $"{module} implementation",
        Module = module,
        DefinitionOfDone = dod,
        CompletedAt = DateTimeOffset.UtcNow
    };

    private static CodeArtifact MakeArtifact(ArtifactLayer layer, string fileName, string modulePath,
        string content, string tracedId = "") => new()
    {
        Id = Guid.NewGuid().ToString("N")[..8],
        Layer = layer,
        FileName = fileName,
        RelativePath = $"{modulePath}/{fileName}",
        Namespace = $"GNex.{modulePath}",
        Content = content,
        ProducedBy = layer switch
        {
            ArtifactLayer.Database => AgentType.Database,
            ArtifactLayer.Service => AgentType.ServiceLayer,
            ArtifactLayer.Test => AgentType.Testing,
            ArtifactLayer.Integration => AgentType.Integration,
            _ => AgentType.Orchestrator
        },
        TracedRequirementIds = string.IsNullOrEmpty(tracedId) ? [] : [tracedId]
    };

    private static AgentContext CreateContext(ExpandedRequirement item, List<CodeArtifact> artifacts)
    {
        var context = new AgentContext();
        context.ExpandedRequirements.Add(item);
        foreach (var a in artifacts)
            context.Artifacts.Add(a);
        return context;
    }
}
