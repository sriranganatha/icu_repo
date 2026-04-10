using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.Workflows;

public class ApprovalGateConfig : PlatformEntityBase
{
    [Required] public string StageId { get; set; } = null!;
    [Required] public string GateType { get; set; } = "auto"; // auto | human | hybrid
    public string? ApproversConfigJson { get; set; } // {"roles":["reviewer"],"min_approvals":1}
    public int TimeoutHours { get; set; } = 24;

    public StageDefinition? Stage { get; set; }
}
