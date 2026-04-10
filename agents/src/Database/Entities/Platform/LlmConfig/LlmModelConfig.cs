using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Platform.LlmConfig;

public class LlmModelConfig : PlatformEntityBase
{
    [Required] public string ProviderId { get; set; } = null!;
    [Required] public string ModelName { get; set; } = null!; // e.g. "gemini-2.5-pro", "gpt-4o", "claude-3.5-sonnet"
    public int ContextWindow { get; set; } = 128000;
    public decimal CostInputPer1kTokens { get; set; }
    public decimal CostOutputPer1kTokens { get; set; }
    [Required] public string CapabilitiesJson { get; set; } = "[]"; // ["code","reasoning","vision"]

    public LlmProviderConfig? Provider { get; set; }
}
