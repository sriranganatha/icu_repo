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
            _logger.LogInformation("SmartLlmRouter: routing [{Agent}] to Gemini ({Provider})",
                prompt.RequestingAgent, _gemini.ProviderName);
            var result = await _gemini.GenerateAsync(prompt, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
            {
                _logger.LogInformation("SmartLlmRouter: Gemini returned {Len} chars for [{Agent}]",
                    result.Content.Length, prompt.RequestingAgent);
                return result;
            }

            _logger.LogWarning("Gemini call failed for [{Agent}], falling back to template. Error: {Error}",
                prompt.RequestingAgent, result.Error);
        }
        else
        {
            _logger.LogWarning("SmartLlmRouter: Gemini not available for [{Agent}], using template fallback",
                prompt.RequestingAgent);
        }

        var fallbackResult = await _fallback.GenerateAsync(prompt, ct);
        _logger.LogInformation("SmartLlmRouter: Template fallback returned {Len} chars for [{Agent}]",
            fallbackResult.Content.Length, prompt.RequestingAgent);
        return fallbackResult;
    }
}
