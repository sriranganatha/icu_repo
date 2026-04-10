using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class AgentRun : PlatformEntityBase
{
    [Required] public string AssignmentId { get; set; } = null!;
    public int RunNumber { get; set; } = 1;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public int TokensUsed { get; set; }
    public int DurationMs { get; set; }
    [Required] public string Status { get; set; } = "running"; // running | succeeded | failed
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public AgentAssignment? Assignment { get; set; }
    public ICollection<AgentArtifactRecord> Artifacts { get; set; } = [];
    public ICollection<AgentConversation> Conversations { get; set; } = [];
}
