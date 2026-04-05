using HmsAgents.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Llm;

/// <summary>
/// Routes LLM requests to the best available provider.
/// Tries Gemini first; falls back to TemplateFallback if unavailable or on error.
/// </summary>
public sealed class SmartLlmRouter : ILlmProvider
{
    private readonly GeminiLlmProvider _gemini;
    private readonly TemplateFallbackLlmProvider _fallback;
    private readonly ILogger<SmartLlmRouter> _logger;

    public string ProviderName => _gemini.IsAvailable ? _gemini.ProviderName : _fallback.ProviderName;
    public bool IsAvailable => true; // Always available — fallback guarantees it

    public SmartLlmRouter(
        GeminiLlmProvider gemini,
        TemplateFallbackLlmProvider fallback,
        ILogger<SmartLlmRouter> logger)
    {
        _gemini = gemini;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        if (_gemini.IsAvailable)
        {
            var result = await _gemini.GenerateAsync(prompt, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
                return result;

            _logger.LogWarning("Gemini call failed for [{Agent}], falling back to template. Error: {Error}",
                prompt.RequestingAgent, result.Error);
        }

        return await _fallback.GenerateAsync(prompt, ct);
    }
}
