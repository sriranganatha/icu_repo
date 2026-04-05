using HmsAgents.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Llm;

/// <summary>
/// Routes LLM requests to the best available provider.
/// Tries OpenAI first; falls back to TemplateFallback if unavailable or on error.
/// </summary>
public sealed class SmartLlmRouter : ILlmProvider
{
    private readonly OpenAiLlmProvider _openAi;
    private readonly TemplateFallbackLlmProvider _fallback;
    private readonly ILogger<SmartLlmRouter> _logger;

    public string ProviderName => _openAi.IsAvailable ? _openAi.ProviderName : _fallback.ProviderName;
    public bool IsAvailable => true; // Always available — fallback guarantees it

    public SmartLlmRouter(
        OpenAiLlmProvider openAi,
        TemplateFallbackLlmProvider fallback,
        ILogger<SmartLlmRouter> logger)
    {
        _openAi = openAi;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        if (_openAi.IsAvailable)
        {
            var result = await _openAi.GenerateAsync(prompt, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
                return result;

            _logger.LogWarning("OpenAI call failed for [{Agent}], falling back to template. Error: {Error}",
                prompt.RequestingAgent, result.Error);
        }

        return await _fallback.GenerateAsync(prompt, ct);
    }
}
