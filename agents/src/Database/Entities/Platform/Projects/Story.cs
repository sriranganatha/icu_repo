using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class Story : PlatformEntityBase
{
    [Required] public string EpicId { get; set; } = null!;
    [Required] public string Title { get; set; } = null!;
    public string AcceptanceCriteriaJson { get; set; } = "[]";
    public int? StoryPoints { get; set; }
    public string? SprintId { get; set; }
    [Required] public string Status { get; set; } = "backlog"; // backlog | ready | in_progress | review | done
    public int Order { get; set; }

    public Epic? Epic { get; set; }
    public Sprint? Sprint { get; set; }
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
