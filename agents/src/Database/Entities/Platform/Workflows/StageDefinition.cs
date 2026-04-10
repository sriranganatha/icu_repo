using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.Workflows;

public class StageDefinition : PlatformEntityBase
{
    [Required] public string WorkflowId { get; set; } = null!;
    [Required] public string Name { get; set; } = null!;
    public int Order { get; set; }
    public string? EntryCriteria { get; set; }
    public string? ExitCriteria { get; set; }
    [Required] public string AgentsInvolvedJson { get; set; } = "[]"; // ["Database","ServiceLayer","Testing"]

    public SdlcWorkflow? Workflow { get; set; }
    public ICollection<ApprovalGateConfig> ApprovalGates { get; set; } = [];
    public ICollection<TransitionRule> TransitionsFrom { get; set; } = [];
    public ICollection<TransitionRule> TransitionsTo { get; set; } = [];
}
