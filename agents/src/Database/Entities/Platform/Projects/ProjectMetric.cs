using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class ProjectMetric : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    [Required] public string MetricType { get; set; } = null!; // velocity | burndown | token_usage | cost | quality_trend
    public decimal Value { get; set; }
    public string? DimensionsJson { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project? Project { get; set; }
}
