using GNex.Core.Models;

namespace GNex.Agents.Brd;

/// <summary>
/// Manages the BRD review/approval lifecycle: Draft → InReview → Approved/Rejected.
/// </summary>
public static class BrdReviewWorkflow
{
    public static bool SubmitForReview(BrdDocument brd, string reviewer)
    {
        if (brd.Status != BrdStatus.Draft) return false;
        brd.Status = BrdStatus.InReview;
        brd.ReviewComments.Add(new BrdComment
        {
            Author = reviewer,
            Content = "Submitted for review.",
            Action = BrdCommentAction.Comment
        });
        return true;
    }

    public static bool Approve(BrdDocument brd, string reviewer, string? comment = null)
    {
        if (brd.Status != BrdStatus.InReview) return false;
        brd.Status = BrdStatus.Approved;
        brd.ApprovedAt = DateTimeOffset.UtcNow;
        brd.ReviewComments.Add(new BrdComment
        {
            Author = reviewer,
            Content = comment ?? "Approved.",
            Action = BrdCommentAction.Approve
        });
        return true;
    }

    public static bool Reject(BrdDocument brd, string reviewer, string reason)
    {
        if (brd.Status != BrdStatus.InReview) return false;
        brd.Status = BrdStatus.Rejected;
        brd.ReviewComments.Add(new BrdComment
        {
            Author = reviewer,
            Content = reason,
            Action = BrdCommentAction.Reject
        });
        return true;
    }

    public static bool RequestChanges(BrdDocument brd, string reviewer, string feedback)
    {
        if (brd.Status != BrdStatus.InReview) return false;
        brd.Status = BrdStatus.Draft;
        brd.ReviewComments.Add(new BrdComment
        {
            Author = reviewer,
            Content = feedback,
            Action = BrdCommentAction.RequestChanges
        });
        return true;
    }

    public static bool Supersede(BrdDocument brd, string reason)
    {
        brd.Status = BrdStatus.Superseded;
        brd.ReviewComments.Add(new BrdComment
        {
            Author = "system",
            Content = reason,
            Action = BrdCommentAction.Comment
        });
        return true;
    }

    /// <summary>
    /// Validates a BRD is complete enough for review — checks all required sections.
    /// </summary>
    public static List<string> ValidateForReview(BrdDocument brd)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(brd.ExecutiveSummary)) issues.Add("Executive Summary is empty.");
        if (string.IsNullOrWhiteSpace(brd.ProjectScope)) issues.Add("Project Scope is empty.");
        if (brd.FunctionalRequirements.Count == 0) issues.Add("No functional requirements listed.");
        if (brd.Stakeholders.Count == 0) issues.Add("No stakeholders identified.");
        if (brd.TracedRequirementIds.Count == 0) issues.Add("No requirements traced.");
        if (brd.Risks.Count == 0) issues.Add("No risks identified.");
        if (brd.SecurityRequirements.Count == 0) issues.Add("No security requirements specified.");
        return issues;
    }
}
