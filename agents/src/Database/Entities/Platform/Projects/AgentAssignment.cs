using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class AgentAssignment : PlatformEntityBase
{
    [Required] public string TaskId { get; set; } = null!;
    [Required] public string AgentTypeDefinitionId { get; set; } = null!;
    [Required] public string Status { get; set; } = "pending"; // pending | running | completed | failed | cancelled
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    public TaskItem? Task { get; set; }
    public ICollection<AgentRun> Runs { get; set; } = [];
}
