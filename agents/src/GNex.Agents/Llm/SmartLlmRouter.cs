using GNex.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Llm;

/// <summary>
/// Routes LLM requests to the best available provider.
/// Priority: Claude (primary) → Gemini (secondary).
/// Returns a clear failure when no provider can fulfil the request.
/// </summary>
public sealed class SmartLlmRouter : ILlmProvider
{
    private readonly ClaudeLlmProvider _claude;
    private readonly GeminiLlmProvider _gemini;
    private readonly ILogger<SmartLlmRouter> _logger;

    public string ProviderName =>
        _claude.IsAvailable ? _claude.ProviderName :
        _gemini.IsAvailable ? _gemini.ProviderName :
        "None";

    public bool IsAvailable => _claude.IsAvailable || _gemini.IsAvailable;

    public SmartLlmRouter(
        ClaudeLlmProvider claude,
        GeminiLlmProvider gemini,
        ILogger<SmartLlmRouter> logger)
    {
        _claude = claude;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        string? lastError = null;

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

                lastError = result.Error;
                _logger.LogWarning("Claude call failed for [{Agent}], trying Gemini. Error: {Error}",
                    prompt.RequestingAgent, result.Error);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(ex, "SmartLlmRouter: Claude threw for [{Agent}], trying Gemini",
                    prompt.RequestingAgent);
            }
        }

        // ── 2. Try Gemini (secondary) with retry on empty content ──
        if (_gemini.IsAvailable)
        {
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation("SmartLlmRouter: routing [{Agent}] to Gemini ({Provider}), attempt {Attempt}/{Max}",
                        prompt.RequestingAgent, _gemini.ProviderName, attempt, maxRetries);
                    var result = await _gemini.GenerateAsync(prompt, ct);
                    if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
                    {
                        _logger.LogInformation("SmartLlmRouter: Gemini returned {Len} chars for [{Agent}] on attempt {Attempt}",
                            result.Content.Length, prompt.RequestingAgent, attempt);
                        return result;
                    }

                    lastError = result.Error ?? "Gemini returned empty content";
                    _logger.LogWarning("Gemini returned empty for [{Agent}] on attempt {Attempt}/{Max}. Error: {Error}",
                        prompt.RequestingAgent, attempt, maxRetries, lastError);

                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s
                        _logger.LogInformation("SmartLlmRouter: retrying Gemini in {Delay}s for [{Agent}]",
                            delay.TotalSeconds, prompt.RequestingAgent);
                        await Task.Delay(delay, ct);
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogWarning(ex, "SmartLlmRouter: Gemini threw for [{Agent}] on attempt {Attempt}",
                        prompt.RequestingAgent, attempt);
                    if (attempt < maxRetries)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
        }

        // ── No providers succeeded — return explicit failure ──
        _logger.LogError("SmartLlmRouter: ALL LLM providers failed for [{Agent}]. Last error: {Error}",
            prompt.RequestingAgent, lastError ?? "No providers available");

        return new LlmResponse
        {
            Success = false,
            Content = "",
            Error = $"All LLM providers failed for [{prompt.RequestingAgent}]. Last error: {lastError ?? "No providers configured"}",
            Model = "none"
        };
    }
}
