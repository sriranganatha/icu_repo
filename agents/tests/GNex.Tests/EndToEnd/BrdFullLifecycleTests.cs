using FluentAssertions;
using GNex.Agents.Brd;
using GNex.Core.Models;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Full BRD lifecycle tests — creation, validation, review workflow state machine,
/// comment trail, diagram sections, and edge cases.
/// </summary>
public class BrdFullLifecycleTests
{
    // ── Creation defaults ──

    [Fact]
    public void BrdDocument_CreatedWithDraftStatus()
    {
        var brd = new BrdDocument { Title = "Test BRD" };
        brd.Status.Should().Be(BrdStatus.Draft);
    }

    [Fact]
    public void BrdDocument_HasUniqueId()
    {
        var a = new BrdDocument();
        var b = new BrdDocument();
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void BrdDocument_CreatedAt_IsRecentUtc()
    {
        var brd = new BrdDocument();
        brd.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BrdDocument_AllListsDefaultEmpty()
    {
        var brd = new BrdDocument();
        brd.Stakeholders.Should().BeEmpty();
        brd.FunctionalRequirements.Should().BeEmpty();
        brd.NonFunctionalRequirements.Should().BeEmpty();
        brd.Assumptions.Should().BeEmpty();
        brd.Constraints.Should().BeEmpty();
        brd.IntegrationPoints.Should().BeEmpty();
        brd.SecurityRequirements.Should().BeEmpty();
        brd.PerformanceRequirements.Should().BeEmpty();
        brd.DataRequirements.Should().BeEmpty();
        brd.Risks.Should().BeEmpty();
        brd.Dependencies.Should().BeEmpty();
        brd.ReviewComments.Should().BeEmpty();
        brd.TracedRequirementIds.Should().BeEmpty();
    }

    // ── State machine — valid transitions ──

    [Fact]
    public void Workflow_DraftToInReview()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.SubmitForReview(brd, "reviewer@test.com").Should().BeTrue();
        brd.Status.Should().Be(BrdStatus.InReview);
    }

    [Fact]
    public void Workflow_InReviewToApproved()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.SubmitForReview(brd, "reviewer");
        BrdReviewWorkflow.Approve(brd, "reviewer", "LGTM").Should().BeTrue();
        brd.Status.Should().Be(BrdStatus.Approved);
        brd.ApprovedAt.Should().NotBeNull();
    }

    [Fact]
    public void Workflow_InReviewToRejected()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.SubmitForReview(brd, "reviewer");
        BrdReviewWorkflow.Reject(brd, "reviewer", "Missing security section").Should().BeTrue();
        brd.Status.Should().Be(BrdStatus.Rejected);
    }

    [Fact]
    public void Workflow_InReviewToDraft_ViaRequestChanges()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.SubmitForReview(brd, "reviewer");
        BrdReviewWorkflow.RequestChanges(brd, "reviewer", "Add more detail to scope").Should().BeTrue();
        brd.Status.Should().Be(BrdStatus.Draft);
    }

    [Fact]
    public void Workflow_Supersede_FromAnyStatus()
    {
        foreach (var status in Enum.GetValues<BrdStatus>())
        {
            var brd = new BrdDocument { Status = status };
            BrdReviewWorkflow.Supersede(brd, "Replaced by newer version").Should().BeTrue();
            brd.Status.Should().Be(BrdStatus.Superseded);
        }
    }

    // ── State machine — invalid transitions ──

    [Fact]
    public void Workflow_CannotSubmitForReview_WhenNotDraft()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.SubmitForReview(brd, "r1");
        // Now InReview — cannot submit again
        BrdReviewWorkflow.SubmitForReview(brd, "r2").Should().BeFalse();
    }

    [Fact]
    public void Workflow_CannotApprove_WhenDraft()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.Approve(brd, "reviewer").Should().BeFalse();
    }

    [Fact]
    public void Workflow_CannotApprove_WhenAlreadyApproved()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.SubmitForReview(brd, "r1");
        BrdReviewWorkflow.Approve(brd, "r1");
        BrdReviewWorkflow.Approve(brd, "r2").Should().BeFalse();
    }

    [Fact]
    public void Workflow_CannotReject_WhenDraft()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.Reject(brd, "reviewer", "reason").Should().BeFalse();
    }

    [Fact]
    public void Workflow_CannotRequestChanges_WhenDraft()
    {
        var brd = CreatePopulatedBrd();
        BrdReviewWorkflow.RequestChanges(brd, "reviewer", "feedback").Should().BeFalse();
    }

    // ── Multi-round review cycle ──

    [Fact]
    public void Workflow_MultiRoundReview_DraftToRevisedToApproved()
    {
        var brd = CreatePopulatedBrd();

        // Round 1: Submit → Request changes
        BrdReviewWorkflow.SubmitForReview(brd, "reviewer").Should().BeTrue();
        BrdReviewWorkflow.RequestChanges(brd, "reviewer", "Fix scope").Should().BeTrue();
        brd.Status.Should().Be(BrdStatus.Draft);

        // Round 2: Resubmit → Approve
        BrdReviewWorkflow.SubmitForReview(brd, "reviewer").Should().BeTrue();
        BrdReviewWorkflow.Approve(brd, "reviewer", "Good now").Should().BeTrue();
        brd.Status.Should().Be(BrdStatus.Approved);

        // Comment trail should have all 4 comments
        brd.ReviewComments.Should().HaveCount(4);
        brd.ReviewComments[0].Action.Should().Be(BrdCommentAction.Comment);
        brd.ReviewComments[1].Action.Should().Be(BrdCommentAction.RequestChanges);
        brd.ReviewComments[2].Action.Should().Be(BrdCommentAction.Comment);
        brd.ReviewComments[3].Action.Should().Be(BrdCommentAction.Approve);
    }

    // ── Validation ──

    [Fact]
    public void Validation_EmptyBrd_ReturnsAllIssues()
    {
        var brd = new BrdDocument();
        var issues = BrdReviewWorkflow.ValidateForReview(brd);

        issues.Should().HaveCountGreaterOrEqualTo(7);
        issues.Should().Contain(i => i.Contains("Executive Summary"));
        issues.Should().Contain(i => i.Contains("Project Scope"));
        issues.Should().Contain(i => i.Contains("functional requirements"));
        issues.Should().Contain(i => i.Contains("stakeholders"));
        issues.Should().Contain(i => i.Contains("requirements traced"));
        issues.Should().Contain(i => i.Contains("risks"));
        issues.Should().Contain(i => i.Contains("security requirements"));
    }

    [Fact]
    public void Validation_FullyPopulatedBrd_NoIssues()
    {
        var brd = CreatePopulatedBrd();
        var issues = BrdReviewWorkflow.ValidateForReview(brd);
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validation_PartiallyPopulated_SpecificIssues()
    {
        var brd = new BrdDocument
        {
            ExecutiveSummary = "Summary",
            ProjectScope = "Scope",
            FunctionalRequirements = ["FR-1"],
            // Missing: Stakeholders, TracedRequirementIds, Risks, SecurityRequirements
        };

        var issues = BrdReviewWorkflow.ValidateForReview(brd);
        issues.Should().HaveCount(4);
    }

    // ── BRD Risk model ──

    [Fact]
    public void BrdRisk_Defaults()
    {
        var risk = new BrdRisk();
        risk.Impact.Should().Be("Medium");
        risk.Likelihood.Should().Be("Medium");
    }

    [Fact]
    public void BrdRisk_FullyPopulated()
    {
        var risk = new BrdRisk
        {
            Description = "Data breach risk",
            Impact = "Critical",
            Likelihood = "Low",
            Mitigation = "Implement encryption at rest"
        };

        risk.Description.Should().Contain("Data breach");
        risk.Impact.Should().Be("Critical");
    }

    // ── Diagram sections ──

    [Fact]
    public void BrdDocument_DiagramSections_DefaultEmpty()
    {
        var brd = new BrdDocument();
        brd.ContextDiagram.Should().BeEmpty();
        brd.DataFlowDiagram.Should().BeEmpty();
        brd.SequenceDiagram.Should().BeEmpty();
        brd.ErDiagram.Should().BeEmpty();
    }

    [Fact]
    public void BrdDocument_MermaidDiagrams_Stored()
    {
        var brd = new BrdDocument
        {
            ContextDiagram = "graph TD\n  User --> System",
            ErDiagram = "erDiagram\n  PATIENT ||--o{ ENCOUNTER : has"
        };

        brd.ContextDiagram.Should().StartWith("graph");
        brd.ErDiagram.Should().StartWith("erDiagram");
    }

    // ── Comment model ──

    [Fact]
    public void BrdComment_DefaultValues()
    {
        var c = new BrdComment();
        c.Author.Should().Be("system");
        c.Action.Should().Be(BrdCommentAction.Comment);
        c.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── Helper ──

    private static BrdDocument CreatePopulatedBrd() => new()
    {
        Title = "HMS Patient Module BRD",
        ExecutiveSummary = "Complete patient management system",
        ProjectScope = "Patient registration, encounters, demographics",
        FunctionalRequirements = ["Patient CRUD", "Encounter tracking"],
        Stakeholders = ["Product Owner", "Lead Developer"],
        TracedRequirementIds = ["REQ-001", "REQ-002"],
        Risks = [new BrdRisk { Description = "Scope creep" }],
        SecurityRequirements = ["RBAC", "Data encryption"]
    };
}
