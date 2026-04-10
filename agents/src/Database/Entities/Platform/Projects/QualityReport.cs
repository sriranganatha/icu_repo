using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Projects;

public class QualityReport : PlatformEntityBase
{
    [Required] public string ProjectId { get; set; } = null!;
    public string? SprintId { get; set; }
    public decimal? CoveragePercent { get; set; }
    public int? LintErrors { get; set; }
    public int? LintWarnings { get; set; }
    public decimal? ComplexityScore { get; set; }
    public int? SecurityVulnerabilities { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project? Project { get; set; }
    public Sprint? Sprint { get; set; }
}
