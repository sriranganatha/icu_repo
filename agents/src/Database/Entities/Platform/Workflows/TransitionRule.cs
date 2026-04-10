using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Workflows;

public class TransitionRule : PlatformEntityBase
{
    [Required] public string FromStageId { get; set; } = null!;
    [Required] public string ToStageId { get; set; } = null!;
    public string? ConditionsJson { get; set; } // {"all_tests_pass":true,"coverage_min":80}
    public bool AutoTransition { get; set; } = true;

    public StageDefinition? FromStage { get; set; }
    public StageDefinition? ToStage { get; set; }
}
