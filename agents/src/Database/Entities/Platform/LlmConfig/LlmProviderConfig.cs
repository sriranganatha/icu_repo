using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Platform.LlmConfig;

public class LlmProviderConfig : PlatformEntityBase
{
    [Required] public string Name { get; set; } = null!; // gemini | openai | anthropic | ollama
    [Required] public string ApiBaseUrl { get; set; } = null!;
    [Required] public string AuthType { get; set; } = "api_key"; // api_key | bearer | none
    public int RateLimitPerMinute { get; set; } = 60;
    public bool IsAvailable { get; set; } = true;

    public ICollection<LlmModelConfig> Models { get; set; } = [];
}
