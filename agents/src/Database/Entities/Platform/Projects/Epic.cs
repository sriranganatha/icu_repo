using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class Epic : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    public string? BrdSectionId { get; set; }
    [Required] public string Title { get; set; } = null!;
    public string? Description { get; set; }
    [Required] public string Priority { get; set; } = "medium"; // critical | high | medium | low
    [Required] public string Status { get; set; } = "draft"; // draft | ready | in_progress | done
    public int Order { get; set; }

    public Project? Project { get; set; }
    public BrdSectionRecord? BrdSection { get; set; }
    public ICollection<Story> Stories { get; set; } = [];
}
