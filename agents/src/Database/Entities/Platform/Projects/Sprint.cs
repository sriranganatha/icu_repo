using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Projects;

public class Sprint : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string Name { get; set; } = null!;
    public string? Goal { get; set; }
    public int Order { get; set; }
    [Required] public string Status { get; set; } = "planning"; // planning | active | review | completed
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }

    public Project? Project { get; set; }
    public ICollection<Story> Stories { get; set; } = [];
    public ICollection<QualityReport> QualityReports { get; set; } = [];
}
