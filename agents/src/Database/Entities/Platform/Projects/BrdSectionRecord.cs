using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class BrdSectionRecord : PlatformEntityBase
{
    [Required] public string BrdId { get; set; } = null!;
    [Required] public string SectionType { get; set; } = null!;
    public int Order { get; set; }
    [Required] public string Content { get; set; } = string.Empty;
    public string DiagramsJson { get; set; } = "[]";

    public Project? Brd { get; set; }
    public ICollection<BrdFeedbackRecord> Feedbacks { get; set; } = [];
}
