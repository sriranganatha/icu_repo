namespace HmsAgents.Core.Models;

/// <summary>
/// Business Requirement Document generated from approved requirements.
/// </summary>
public sealed class BrdDocument
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RunId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public BrdStatus Status { get; set; } = BrdStatus.Draft;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }

    // ── BRD Sections ──
    public string ExecutiveSummary { get; set; } = string.Empty;
    public string ProjectScope { get; set; } = string.Empty;
    public string InScope { get; set; } = string.Empty;
    public string OutOfScope { get; set; } = string.Empty;
    public List<string> Stakeholders { get; set; } = [];
    public List<string> FunctionalRequirements { get; set; } = [];
    public List<string> NonFunctionalRequirements { get; set; } = [];
    public List<string> Assumptions { get; set; } = [];
    public List<string> Constraints { get; set; } = [];
    public List<string> IntegrationPoints { get; set; } = [];
    public List<string> SecurityRequirements { get; set; } = [];
    public List<string> PerformanceRequirements { get; set; } = [];
    public List<string> DataRequirements { get; set; } = [];
    public List<BrdRisk> Risks { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];

    // ── Diagrams (Mermaid markdown) ──
    public string ContextDiagram { get; set; } = string.Empty;
    public string DataFlowDiagram { get; set; } = string.Empty;
    public string SequenceDiagram { get; set; } = string.Empty;
    public string ErDiagram { get; set; } = string.Empty;

    // ── Approvals ──
    public List<BrdComment> ReviewComments { get; set; } = [];
    public List<string> TracedRequirementIds { get; set; } = [];
}

public enum BrdStatus
{
    Draft,
    InReview,
    Approved,
    Rejected,
    Superseded
}

public sealed class BrdRisk
{
    public string Description { get; init; } = string.Empty;
    public string Impact { get; init; } = "Medium";
    public string Likelihood { get; init; } = "Medium";
    public string Mitigation { get; init; } = string.Empty;
}

public sealed class BrdComment
{
    public string Author { get; init; } = "system";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Content { get; init; } = string.Empty;
    public BrdCommentAction Action { get; init; } = BrdCommentAction.Comment;
}

public enum BrdCommentAction
{
    Comment,
    Approve,
    Reject,
    RequestChanges
}
