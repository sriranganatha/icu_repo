using HmsAgents.Agents.Brd;
using HmsAgents.Core.Models;

namespace HmsAgents.Tests;

public class BrdReviewWorkflowTests
{
    private static BrdDocument CreateDraft() => new() { Title = "Test BRD" };

    [Fact]
    public void SubmitForReview_Draft_TransitionsToInReview()
    {
        var brd = CreateDraft();
        var result = BrdReviewWorkflow.SubmitForReview(brd, "alice");

        Assert.True(result);
        Assert.Equal(BrdStatus.InReview, brd.Status);
        Assert.Single(brd.ReviewComments);
    }

    [Fact]
    public void SubmitForReview_NonDraft_ReturnsFalse()
    {
        var brd = CreateDraft();
        brd.Status = BrdStatus.Approved;
        Assert.False(BrdReviewWorkflow.SubmitForReview(brd, "alice"));
    }

    [Fact]
    public void Approve_InReview_TransitionsToApproved()
    {
        var brd = CreateDraft();
        BrdReviewWorkflow.SubmitForReview(brd, "alice");
        var result = BrdReviewWorkflow.Approve(brd, "bob", "All good");

        Assert.True(result);
        Assert.Equal(BrdStatus.Approved, brd.Status);
        Assert.NotNull(brd.ApprovedAt);
        Assert.Equal(2, brd.ReviewComments.Count);
    }

    [Fact]
    public void Approve_Draft_ReturnsFalse()
    {
        var brd = CreateDraft();
        Assert.False(BrdReviewWorkflow.Approve(brd, "bob"));
    }

    [Fact]
    public void Reject_InReview_TransitionsToRejected()
    {
        var brd = CreateDraft();
        BrdReviewWorkflow.SubmitForReview(brd, "alice");
        var result = BrdReviewWorkflow.Reject(brd, "carol", "Missing scope");

        Assert.True(result);
        Assert.Equal(BrdStatus.Rejected, brd.Status);
    }

    [Fact]
    public void RequestChanges_InReview_RevertsToDraft()
    {
        var brd = CreateDraft();
        BrdReviewWorkflow.SubmitForReview(brd, "alice");
        var result = BrdReviewWorkflow.RequestChanges(brd, "dave", "Add more detail");

        Assert.True(result);
        Assert.Equal(BrdStatus.Draft, brd.Status);
    }

    [Fact]
    public void Supersede_AnyStatus_TransitionsToSuperseded()
    {
        var brd = CreateDraft();
        BrdReviewWorkflow.Supersede(brd, "Replaced by v2");
        Assert.Equal(BrdStatus.Superseded, brd.Status);
    }

    [Fact]
    public void ValidateForReview_CompleteBrd_NoIssues()
    {
        var brd = new BrdDocument
        {
            ExecutiveSummary = "Summary",
            ProjectScope = "Scope",
            Stakeholders = ["Admin"],
            FunctionalRequirements = ["FR1"],
            Risks = [new BrdRisk { Description = "R1" }],
            SecurityRequirements = ["SR1"],
            TracedRequirementIds = ["REQ-1"]
        };

        var issues = BrdReviewWorkflow.ValidateForReview(brd);
        Assert.Empty(issues);
    }

    [Fact]
    public void ValidateForReview_EmptyBrd_ReturnsAllIssues()
    {
        var brd = new BrdDocument();
        var issues = BrdReviewWorkflow.ValidateForReview(brd);

        Assert.True(issues.Count >= 5);
        Assert.Contains(issues, i => i.Contains("Executive Summary"));
        Assert.Contains(issues, i => i.Contains("stakeholders"));
    }

    [Fact]
    public void FullWorkflow_DraftToApproved()
    {
        var brd = CreateDraft();

        Assert.Equal(BrdStatus.Draft, brd.Status);
        BrdReviewWorkflow.SubmitForReview(brd, "alice");
        Assert.Equal(BrdStatus.InReview, brd.Status);
        BrdReviewWorkflow.RequestChanges(brd, "bob", "Fix scope");
        Assert.Equal(BrdStatus.Draft, brd.Status);
        BrdReviewWorkflow.SubmitForReview(brd, "alice");
        Assert.Equal(BrdStatus.InReview, brd.Status);
        BrdReviewWorkflow.Approve(brd, "bob", "LGTM");
        Assert.Equal(BrdStatus.Approved, brd.Status);
        Assert.Equal(4, brd.ReviewComments.Count);
    }
}
