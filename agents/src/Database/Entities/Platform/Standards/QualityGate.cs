using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Standards;

public class QualityGate : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!;
    [Required] public string GateType { get; set; } = null!; // coverage | complexity | duplication | review_score | security
    [Required] public string ThresholdConfigJson { get; set; } = "{}"; // {"min_coverage":80,"max_complexity":15,"max_duplication":5}
}
