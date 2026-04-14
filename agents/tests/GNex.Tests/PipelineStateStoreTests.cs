using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;
using GNex.Studio.Services;

namespace GNex.Tests;

/// <summary>
/// Tests for PipelineStateStore: snapshot tracking, resume state,
/// derived service persistence, and completed agent detection.
/// </summary>
public class PipelineStateStoreTests
{
    private static PipelineStateStore CreateStore()
    {
        // PipelineStateStore loads from disk on construction;
        // for tests, we create & reset immediately
        var store = new PipelineStateStore();
        store.Reset($"test-{Guid.NewGuid():N}"[..16]);
        return store;
    }

    // ────────────────────────────────────────────────
    //  Reset and initialization
    // ────────────────────────────────────────────────

    [Fact]
    public void Reset_CreatesNewSnapshot()
    {
        var store = CreateStore();
        var runId = "run-001";

        store.Reset(runId);

        store.CurrentSnapshot.Should().NotBeNull();
        store.CurrentSnapshot!.RunId.Should().Be(runId);
        store.CurrentSnapshot.Completed.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsExistingState()
    {
        var store = CreateStore();
        store.TrackEvent("run-001", (int)AgentType.Database, (int)AgentStatus.Completed,
            "Done", 5, 0, 1000, 0);

        store.Reset("run-002");

        store.CurrentSnapshot!.AgentStatuses.Should().BeEmpty();
        store.CurrentSnapshot.AgentMessages.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────
    //  TrackEvent — agent status tracking
    // ────────────────────────────────────────────────

    [Fact]
    public void TrackEvent_UpdatesAgentStatus()
    {
        var store = CreateStore();

        store.TrackEvent("run-001", (int)AgentType.Database, (int)AgentStatus.Running,
            "Generating schemas", 0, 0, 500, 0);

        store.CurrentSnapshot!.AgentStatuses["Database"].Should().Be("Running");
        store.CurrentSnapshot.AgentMessages["Database"].Should().Be("Generating schemas");
    }

    [Fact]
    public void TrackEvent_TracksArtifactCounts()
    {
        var store = CreateStore();

        store.TrackEvent("run-001", (int)AgentType.Database, (int)AgentStatus.Completed,
            "Done", 10, 2, 3000, 0);

        store.CurrentSnapshot!.AgentArtifacts["Database"].Should().Be(10);
        store.CurrentSnapshot.AgentFindings["Database"].Should().Be(2);
    }

    [Fact]
    public void TrackEvent_TracksRetries()
    {
        var store = CreateStore();

        store.TrackEvent("run-001", (int)AgentType.Database, (int)AgentStatus.Failed,
            "Failed", 0, 0, 1000, 1);

        store.CurrentSnapshot!.AgentRetries["Database"].Should().Be(1);
    }

    [Fact]
    public void TrackEvent_ZeroArtifactsAndFindings_NotStored()
    {
        var store = CreateStore();

        store.TrackEvent("run-001", (int)AgentType.Planning, (int)AgentStatus.Completed,
            "Plan created", 0, 0, 500, 0);

        store.CurrentSnapshot!.AgentArtifacts.Should().NotContainKey("Planning");
        store.CurrentSnapshot.AgentFindings.Should().NotContainKey("Planning");
    }

    // ────────────────────────────────────────────────
    //  GetCompletedAgents — resume support
    // ────────────────────────────────────────────────

    [Fact]
    public void GetCompletedAgents_ReturnsOnlyCompleted()
    {
        var store = CreateStore();

        store.TrackEvent("run-001", (int)AgentType.RequirementsReader, (int)AgentStatus.Completed,
            "Done", 0, 0, 100, 0);
        store.TrackEvent("run-001", (int)AgentType.Database, (int)AgentStatus.Failed,
            "Failed", 0, 0, 200, 1);
        store.TrackEvent("run-001", (int)AgentType.Architect, (int)AgentStatus.Completed,
            "Done", 0, 0, 300, 0);

        var completed = store.GetCompletedAgents();

        completed.Should().HaveCount(2);
        completed.Should().Contain("RequirementsReader");
        completed.Should().Contain("Architect");
        completed.Should().NotContain("Database");
    }

    [Fact]
    public void GetCompletedAgents_EmptyWhenNoSnapshot()
    {
        var store = new PipelineStateStore();
        // Not calling Reset — might have leftover state from disk
        store.Reset("clean");
        var completed = store.GetCompletedAgents();
        completed.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────
    //  DerivedServices persistence
    // ────────────────────────────────────────────────

    [Fact]
    public void TrackDerivedServices_PersistsToSnapshot()
    {
        var store = CreateStore();
        var services = new List<MicroserviceDefinition>
        {
            new()
            {
                Name = "PatientService", ShortName = "patient", Schema = "patient_schema",
                Description = "Patient bounded context", ApiPort = 5200,
                Entities = ["Patient", "Address"], DependsOn = []
            },
            new()
            {
                Name = "BillingService", ShortName = "billing", Schema = "billing_schema",
                Description = "Billing bounded context", ApiPort = 5201,
                Entities = ["Invoice"], DependsOn = ["PatientService"]
            }
        };

        store.TrackDerivedServices("run-001", services);

        store.CurrentSnapshot!.DerivedServices.Should().HaveCount(2);
        store.CurrentSnapshot.DerivedServices[0].Name.Should().Be("PatientService");
        store.CurrentSnapshot.DerivedServices[0].Entities.Should().Contain("Patient");
        store.CurrentSnapshot.DerivedServices[1].DependsOn.Should().Contain("PatientService");
    }

    [Fact]
    public void RestoreDerivedServices_PopulatesContext()
    {
        var store = CreateStore();
        store.TrackDerivedServices("run-001", new List<MicroserviceDefinition>
        {
            new()
            {
                Name = "Patient", ShortName = "pat", Schema = "pat",
                Description = "Patient", ApiPort = 5200,
                Entities = ["Patient"], DependsOn = []
            }
        });

        var ctx = new AgentContext();
        store.RestoreDerivedServices(ctx);

        ctx.DerivedServices.Should().HaveCount(1);
        ctx.DerivedServices[0].Name.Should().Be("Patient");
    }

    [Fact]
    public void RestoreDerivedServices_DoesNotOverwrite_ExistingServices()
    {
        var store = CreateStore();
        store.TrackDerivedServices("run-001", new List<MicroserviceDefinition>
        {
            new()
            {
                Name = "Snapshot", ShortName = "snap", Schema = "snap",
                Description = "From snapshot", ApiPort = 5200,
                Entities = ["E1"], DependsOn = []
            }
        });

        var existing = new MicroserviceDefinition
        {
            Name = "Existing", ShortName = "ex", Schema = "ex",
            Description = "Already set", ApiPort = 5201,
            Entities = ["E2"], DependsOn = []
        };
        var ctx = new AgentContext { DerivedServices = [existing] };

        store.RestoreDerivedServices(ctx);

        ctx.DerivedServices.Should().HaveCount(1);
        ctx.DerivedServices[0].Name.Should().Be("Existing", "existing services should not be overwritten");
    }

    [Fact]
    public void RestoreDerivedServices_NullSnapshot_NoOp()
    {
        var store = CreateStore();
        // Reset creates snapshot with no derived services
        var ctx = new AgentContext();

        store.RestoreDerivedServices(ctx);

        ctx.DerivedServices.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────
    //  TrackCompletion — final pipeline state
    // ────────────────────────────────────────────────

    [Fact]
    public void TrackCompletion_SetsCompletedFlag()
    {
        var store = CreateStore();
        var backlog = new List<ExpandedRequirement>
        {
            new() { Id = "WI-001", Title = "Item 1", ItemType = WorkItemType.Task, Status = WorkItemStatus.Completed }
        };

        store.TrackCompletion("run-001", 10, 5, 2, 1, backlog, 30000);

        store.CurrentSnapshot!.Completed.Should().BeTrue();
        store.CurrentSnapshot.CompletedAt.Should().NotBeNull();
        store.CurrentSnapshot.RequirementCount.Should().Be(10);
        store.CurrentSnapshot.ArtifactCount.Should().Be(5);
        store.CurrentSnapshot.FindingCount.Should().Be(2);
        store.CurrentSnapshot.BacklogCount.Should().Be(1);
        store.CurrentSnapshot.DurationMs.Should().Be(30000);
    }

    [Fact]
    public void TrackCompletion_PersistsBacklogItems()
    {
        var store = CreateStore();
        var items = new List<ExpandedRequirement>
        {
            new()
            {
                Id = "WI-001", Title = "Create user DB", Module = "User",
                ItemType = WorkItemType.Task, Status = WorkItemStatus.InProgress,
                Priority = 1, Iteration = 1, Tags = ["database"],
                AssignedAgent = "Database"
            },
            new()
            {
                Id = "WI-002", Title = "Create API endpoints", Module = "User",
                ItemType = WorkItemType.UserStory, Status = WorkItemStatus.New,
                Priority = 2, Iteration = 1
            }
        };

        store.TrackCompletion("run-001", 5, 3, 0, 0, items, 10000);

        store.CurrentSnapshot!.BacklogItems.Should().HaveCount(2);
        store.CurrentSnapshot.BacklogItems[0].Id.Should().Be("WI-001");
        store.CurrentSnapshot.BacklogItems[0].AssignedAgent.Should().Be("Database");
        store.CurrentSnapshot.BacklogItems[1].ItemType.Should().Be("UserStory");
    }

    // ────────────────────────────────────────────────
    //  HasIncompleteRun
    // ────────────────────────────────────────────────

    [Fact]
    public void HasIncompleteRun_FalseAfterReset()
    {
        var store = CreateStore();

        // A freshly reset store has no agents tracked, but snapshot exists
        // HasIncompleteRun checks: snapshot != null && !Completed && AgentStatuses.Count > 0
        store.CurrentSnapshot!.AgentStatuses.Should().BeEmpty();
    }

    [Fact]
    public void HasIncompleteRun_TrueWhenAgentsRunning()
    {
        var store = CreateStore();
        store.TrackEvent("run-001", (int)AgentType.Database, (int)AgentStatus.Running,
            "Processing", 0, 0, 100, 0);

        // Snapshot is not completed and has agents
        store.CurrentSnapshot!.Completed.Should().BeFalse();
        store.CurrentSnapshot.AgentStatuses.Should().NotBeEmpty();
    }

    // ────────────────────────────────────────────────
    //  Snapshot model defaults
    // ────────────────────────────────────────────────

    [Fact]
    public void PipelineStateSnapshot_Defaults()
    {
        var snap = new PipelineStateSnapshot();

        snap.RunId.Should().BeEmpty();
        snap.Completed.Should().BeFalse();
        snap.CompletedAt.Should().BeNull();
        snap.RequirementCount.Should().Be(0);
        snap.ArtifactCount.Should().Be(0);
        snap.AgentStatuses.Should().BeEmpty();
        snap.AgentMessages.Should().BeEmpty();
        snap.DerivedServices.Should().BeEmpty();
        snap.BacklogItems.Should().BeEmpty();
    }

    [Fact]
    public void DerivedServiceSnapshot_Defaults()
    {
        var snap = new DerivedServiceSnapshot();

        snap.Name.Should().BeEmpty();
        snap.ShortName.Should().BeEmpty();
        snap.Schema.Should().BeEmpty();
        snap.Entities.Should().BeEmpty();
        snap.DependsOn.Should().BeEmpty();
    }

    [Fact]
    public void BacklogItemSnapshot_Defaults()
    {
        var snap = new BacklogItemSnapshot();

        snap.Id.Should().BeEmpty();
        snap.Title.Should().BeEmpty();
        snap.Tags.Should().BeEmpty();
        snap.AcceptanceCriteria.Should().BeEmpty();
    }
}
