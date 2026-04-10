using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class BrdFeedbackRecord : PlatformEntityBase
{
    [Required] public string BrdId { get; set; } = null!;
    public string? SectionId { get; set; }
    [Required] public string FeedbackText { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public int? ResolvedInVersion { get; set; }

    public BrdSectionRecord? Section { get; set; }
}
