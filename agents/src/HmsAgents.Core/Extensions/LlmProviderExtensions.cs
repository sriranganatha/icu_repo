using HmsAgents.Core.Interfaces;

namespace HmsAgents.Core.Extensions;

/// <summary>
/// Convenience extensions for <see cref="ILlmProvider"/> to simplify common call patterns.
/// </summary>
public static class LlmProviderExtensions
{
    /// <summary>
    /// Shorthand: sends a plain-text prompt with default temperature/tokens and returns the response content string.
    /// Throws <see cref="InvalidOperationException"/> if the LLM call fails.
    /// </summary>
    public static async Task<string> GenerateAsync(this ILlmProvider provider, string prompt, CancellationToken ct)
    {
        var response = await provider.GenerateAsync(new LlmPrompt
        {
            UserPrompt = prompt,
            Temperature = 0.2,
            MaxTokens = 4096
        }, ct);

        return response.Success
            ? response.Content
            : throw new InvalidOperationException($"LLM call failed: {response.Error}");
    }
}
