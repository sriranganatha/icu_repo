using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class TaskItem : PlatformEntityBase
{
    [Required] public string StoryId { get; set; } = null!;
    [Required] public string TaskType { get; set; } = null!; // code | test | review | deploy | doc
    public string? AssignedAgentType { get; set; }
    public string DependsOnJson { get; set; } = "[]";
    public int? EstimatedTokens { get; set; }
    [Required] public string Status { get; set; } = "pending"; // pending | assigned | running | review | done | failed
    public int Order { get; set; }

    public Story? Story { get; set; }
    public ICollection<TaskDependency> DependenciesFrom { get; set; } = [];
    public ICollection<TaskDependency> DependenciesTo { get; set; } = [];
    public ICollection<AgentAssignment> Assignments { get; set; } = [];
}
