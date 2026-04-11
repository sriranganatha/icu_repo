using GNex.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Llm;

/// <summary>
/// Routes LLM requests to the best available provider.
/// Priority: Claude (primary) → Gemini (secondary) → TemplateFallback (last resort).
/// </summary>
public sealed class SmartLlmRouter : ILlmProvider
{
    private readonly ClaudeLlmProvider _claude;
    private readonly GeminiLlmProvider _gemini;
    private readonly TemplateFallbackLlmProvider _fallback;
    private readonly ILogger<SmartLlmRouter> _logger;

    public string ProviderName =>
        _claude.IsAvailable ? _claude.ProviderName :
        _gemini.IsAvailable ? _gemini.ProviderName :
        _fallback.ProviderName;

    public bool IsAvailable => true; // Always available — fallback guarantees it

    public SmartLlmRouter(
        ClaudeLlmProvider claude,
        GeminiLlmProvider gemini,
        TemplateFallbackLlmProvider fallback,
        ILogger<SmartLlmRouter> logger)
    {
        _claude = claude;
        _gemini = gemini;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        // ── 1. Try Claude (primary) ──
        if (_claude.IsAvailable)
        {
            try
            {
                _logger.LogInformation("SmartLlmRouter: routing [{Agent}] to Claude ({Provider})",
                    prompt.RequestingAgent, _claude.ProviderName);
                var result = await _claude.GenerateAsync(prompt, ct);
                if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
                {
                    _logger.LogInformation("SmartLlmRouter: Claude returned {Len} chars for [{Agent}]",
                        result.Content.Length, prompt.RequestingAgent);
                    return result;
                }

                _logger.LogWarning("Claude call failed for [{Agent}], trying Gemini. Error: {Error}",
                    prompt.RequestingAgent, result.Error);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SmartLlmRouter: Claude threw for [{Agent}], trying Gemini",
                    prompt.RequestingAgent);
            }
        }

        // ── 2. Try Gemini (secondary) ──
        if (_gemini.IsAvailable)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SmartLlmRouter: Gemini threw for [{Agent}], falling back to template",
                    prompt.RequestingAgent);
            }
        }

        // ── 3. Template fallback (last resort) ──
        if (!_claude.IsAvailable && !_gemini.IsAvailable)
        {
            _logger.LogWarning("SmartLlmRouter: No LLM providers available for [{Agent}], using template fallback",
                prompt.RequestingAgent);
        }

        var fallbackResult = await _fallback.GenerateAsync(prompt, ct);
        _logger.LogInformation("SmartLlmRouter: Template fallback returned {Len} chars for [{Agent}]",
            fallbackResult.Content.Length, prompt.RequestingAgent);
        return fallbackResult;
    }
}
