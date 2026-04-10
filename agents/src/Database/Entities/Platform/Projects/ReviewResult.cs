using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class ReviewResult : PlatformEntityBase
{
    [Required] public string ArtifactId { get; set; } = null!;
    [Required] public string ReviewerAgentType { get; set; } = null!;
    [Required] public string Verdict { get; set; } = "pending"; // approved | rejected | needs_revision | pending
    public string CommentsJson { get; set; } = "[]";
    public int? Score { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;

    public AgentArtifactRecord? Artifact { get; set; }
}
