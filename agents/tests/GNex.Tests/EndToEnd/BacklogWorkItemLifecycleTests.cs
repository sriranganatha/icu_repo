using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Tests for backlog work item lifecycle including status transitions,
/// DOD verification tracking, gap-analysis fields, dependency chains,
/// and work item templates (Epic, UserStory, UseCase, Task, Bug).
/// </summary>
public class BacklogWorkItemLifecycleTests
{
    // ── Status transitions ──

    [Fact]
    public void WorkItem_DefaultStatus_IsNew()
    {
        var item = new ExpandedRequirement { Id = "WI-001" };
        item.Status.Should().Be(WorkItemStatus.New);
    }

    [Theory]
    [InlineData(WorkItemStatus.New)]
    [InlineData(WorkItemStatus.InQueue)]
    [InlineData(WorkItemStatus.Received)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Blocked)]
    public void WorkItem_AllStatusTransitions_Valid(WorkItemStatus status)
    {
        var item = new ExpandedRequirement { Id = "WI-001" };
        item.Status = status;
        item.Status.Should().Be(status);
    }

    [Fact]
    public void WorkItem_UnderDev_IsAliasForInProgress()
    {
        WorkItemStatus.UnderDev.Should().Be(WorkItemStatus.InProgress);
    }

    // ── Full lifecycle simulation ──

    [Fact]
    public void WorkItem_FullLifecycle_NewToCompleted()
    {
        var item = new ExpandedRequirement
        {
            Id = "WI-001", Title = "Implement Patient CRUD",
            ItemType = WorkItemType.Task, Priority = 1
        };

        // New → InQueue (backlog agent queues it)
        item.Status.Should().Be(WorkItemStatus.New);
        item.Status = WorkItemStatus.InQueue;

        // InQueue → Received (orchestrator claims it)
        item.Status = WorkItemStatus.Received;
        item.AssignedAgent = "Database";

        // Received → InProgress (agent starts working)
        item.Status = WorkItemStatus.InProgress;
        item.StartedAt = DateTimeOffset.UtcNow;

        // InProgress → Completed
        item.Status = WorkItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;

        item.CompletedAt.Should().NotBeNull();
        item.CompletedAt!.Value.Should().BeAfter(item.StartedAt!.Value);
    }

    [Fact]
    public void WorkItem_FailureAndRetry()
    {
        var item = new ExpandedRequirement
        {
            Id = "WI-002", Title = "Integration task", ItemType = WorkItemType.Task
        };

        item.Status = WorkItemStatus.InProgress;
        item.AssignedAgent = "Integration";

        // Agent fails
        item.Status = WorkItemStatus.Failed;
        item.LastFailedAgent = "Integration";
        item.RetryCount = 1;

        // Re-queued
        item.Status = WorkItemStatus.InQueue;

        // Second attempt
        item.Status = WorkItemStatus.InProgress;
        item.AssignedAgent = "Integration";
        item.RetryCount = 2;
        item.Status = WorkItemStatus.Completed;

        item.RetryCount.Should().Be(2);
        item.LastFailedAgent.Should().Be("Integration");
    }

    // ── DOD verification ──

    [Fact]
    public void WorkItem_DodVerification_AllPass()
    {
        var item = new ExpandedRequirement
        {
            Id = "WI-003",
            DefinitionOfDone = ["Unit tests pass", "RLS policies applied", "Audit columns present"]
        };

        foreach (var dod in item.DefinitionOfDone)
            item.DodVerificationStatus[dod] = true;

        item.DodVerified = item.DodVerificationStatus.Values.All(v => v);
        item.DodVerified.Should().BeTrue();
    }

    [Fact]
    public void WorkItem_DodVerification_PartialFail()
    {
        var item = new ExpandedRequirement
        {
            Id = "WI-004",
            DefinitionOfDone = ["Unit tests pass", "RLS policies applied"]
        };

        item.DodVerificationStatus["Unit tests pass"] = true;
        item.DodVerificationStatus["RLS policies applied"] = false;
        item.DodVerificationNotes.Add("No RLS policy found for patient table");

        item.DodVerified = item.DodVerificationStatus.Values.All(v => v);
        item.DodVerified.Should().BeFalse();
        item.DodVerificationNotes.Should().HaveCount(1);
    }

    // ── Gap analysis fields ──

    [Fact]
    public void WorkItem_CoverageStatus_DefaultNotAssessed()
    {
        var item = new ExpandedRequirement();
        item.Coverage.Should().Be(CoverageStatus.NotAssessed);
    }

    [Theory]
    [InlineData(CoverageStatus.NotStarted)]
    [InlineData(CoverageStatus.Partial)]
    [InlineData(CoverageStatus.Covered)]
    [InlineData(CoverageStatus.GapIdentified)]
    public void WorkItem_AllCoverageStatuses(CoverageStatus status)
    {
        var item = new ExpandedRequirement { Coverage = status };
        item.Coverage.Should().Be(status);
    }

    [Fact]
    public void WorkItem_GapAnalysisFields_Populated()
    {
        var item = new ExpandedRequirement
        {
            Id = "WI-005",
            Coverage = CoverageStatus.GapIdentified,
            IdentifiedGaps = ["Missing RLS policy", "No unit tests for UpdateClaim"],
            MatchingArtifactPaths = ["Services/ClaimService.cs", "Database/Claim.cs"],
            AffectedServices = ["ClaimService", "PatientService"],
            ProducedBy = "RequirementAnalyzer"
        };

        item.IdentifiedGaps.Should().HaveCount(2);
        item.MatchingArtifactPaths.Should().HaveCount(2);
        item.AffectedServices.Should().Contain("ClaimService");
        item.ProducedBy.Should().Be("RequirementAnalyzer");
    }

    [Fact]
    public void WorkItem_ResolvedDependencyChain_PopulatedCorrectly()
    {
        var item = new ExpandedRequirement
        {
            Id = "WI-006",
            DependsOn = ["WI-001", "WI-003"],
            ResolvedDependencyChain = ["WI-001", "WI-003", "WI-002"] // transitive
        };

        item.ResolvedDependencyChain.Should().Contain("WI-002");
        item.ResolvedDependencyChain.Should().HaveCount(3);
    }

    // ── Work item types (templates) ──

    [Fact]
    public void Epic_TemplateFields()
    {
        var epic = new ExpandedRequirement
        {
            Id = "EPIC-001", ItemType = WorkItemType.Epic,
            Title = "Patient Management Module",
            Summary = "Complete patient lifecycle management",
            BusinessValue = "Core revenue-generating module",
            SuccessCriteria = ["CRUD operations < 200ms", "99.9% uptime"],
            Scope = "Includes: registration, encounters. Excludes: billing"
        };

        epic.Summary.Should().NotBeEmpty();
        epic.BusinessValue.Should().NotBeEmpty();
        epic.SuccessCriteria.Should().HaveCount(2);
        epic.Scope.Should().Contain("Excludes");
    }

    [Fact]
    public void UserStory_TemplateFields()
    {
        var story = new ExpandedRequirement
        {
            Id = "US-001", ItemType = WorkItemType.UserStory,
            Title = "As a nurse, I can register a patient",
            StoryPoints = 5,
            Labels = ["Frontend", "API", "Database"],
            AcceptanceCriteria = ["Given valid data, When I submit, Then patient is created"]
        };

        story.StoryPoints.Should().Be(5);
        story.Labels.Should().Contain("API");
    }

    [Fact]
    public void UseCase_TemplateFields()
    {
        var uc = new ExpandedRequirement
        {
            Id = "UC-001", ItemType = WorkItemType.UseCase,
            Title = "Register Patient",
            Actor = "Nurse",
            Preconditions = "Nurse is authenticated",
            MainFlow = ["Nurse enters patient data", "System validates", "System saves"],
            AlternativeFlows = "If validation fails, show errors",
            Postconditions = "Patient record exists"
        };

        uc.Actor.Should().Be("Nurse");
        uc.MainFlow.Should().HaveCount(3);
    }

    [Fact]
    public void Task_TemplateFields()
    {
        var task = new ExpandedRequirement
        {
            Id = "TASK-001", ItemType = WorkItemType.Task,
            Title = "Create patient migration",
            TechnicalNotes = "Use UUID for primary key, add RLS policy",
            DefinitionOfDone = ["Migration runs", "RLS applied", "Tests pass"]
        };

        task.TechnicalNotes.Should().Contain("RLS");
        task.DefinitionOfDone.Should().HaveCount(3);
    }

    [Fact]
    public void Bug_TemplateFields()
    {
        var bug = new ExpandedRequirement
        {
            Id = "BUG-001", ItemType = WorkItemType.Bug,
            Title = "Patient search returns wrong tenant data",
            Severity = "Critical",
            Environment = "Production",
            StepsToReproduce = ["Login as tenant A", "Search for patient", "See tenant B data"],
            ExpectedResult = "Only tenant A patients shown",
            ActualResult = "Cross-tenant data leakage"
        };

        bug.Severity.Should().Be("Critical");
        bug.StepsToReproduce.Should().HaveCount(3);
    }

    // ── Backlog operations on context ──

    [Fact]
    public void Context_ExpandedRequirements_ThreadSafe()
    {
        var ctx = new AgentContext();
        var items = Enumerable.Range(0, 100).Select(i =>
            new ExpandedRequirement { Id = $"WI-{i:D4}", Title = $"Item {i}" }).ToList();

        Parallel.ForEach(items, item => ctx.ExpandedRequirements.Add(item));

        ctx.ExpandedRequirements.Should().HaveCount(100);
    }

    [Fact]
    public void Context_Artifacts_ThreadSafe()
    {
        var ctx = new AgentContext();
        var artifacts = Enumerable.Range(0, 100).Select(i =>
            new CodeArtifact { FileName = $"File{i}.cs", ProducedBy = AgentType.Database }).ToList();

        Parallel.ForEach(artifacts, a => ctx.Artifacts.Add(a));

        ctx.Artifacts.Should().HaveCount(100);
    }
}
