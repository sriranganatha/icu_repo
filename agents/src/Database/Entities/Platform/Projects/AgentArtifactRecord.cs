using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class AgentArtifactRecord : PlatformEntityBase
{
    [Required] public string RunId { get; set; } = null!;
    [Required] public string ArtifactType { get; set; } = null!; // code | test | doc | config | diagram
    [Required] public string FilePath { get; set; } = null!;
    public string? ContentHash { get; set; }
    [Required] public string ReviewStatus { get; set; } = "pending"; // pending | approved | rejected | needs_revision

    public AgentRun? Run { get; set; }
    public ICollection<ReviewResult> Reviews { get; set; } = [];
}
