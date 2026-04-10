using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.LlmConfig;

public class LlmRoutingRule : PlatformEntityBase
{
    [Required] public string TaskType { get; set; } = null!; // code_generation | review | testing | documentation
    [Required] public string PrimaryModelId { get; set; } = null!;
    public string? FallbackModelId { get; set; }
    public string? ConditionsJson { get; set; } // {"max_tokens":4096,"language":"csharp"}
    public int Priority { get; set; } = 100;
}
