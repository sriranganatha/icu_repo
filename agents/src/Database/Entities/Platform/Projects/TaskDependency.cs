using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class TaskDependency : PlatformEntityBase
{
    [Required] public string TaskId { get; set; } = null!;
    [Required] public string DependsOnTaskId { get; set; } = null!;
    [Required] public string DependencyType { get; set; } = "finish_to_start"; // finish_to_start | start_to_start | finish_to_finish

    public TaskItem? Task { get; set; }
    public TaskItem? DependsOnTask { get; set; }
}
