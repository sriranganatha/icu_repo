namespace GNex.Core.Interfaces;

/// <summary>
/// Abstraction for LLM-powered code generation. All agents use this to produce
/// production-grade code artifacts. Implementations can target OpenAI, Azure OpenAI,
/// Ollama, or a template-based fallback for offline/test scenarios.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Generate code/text from a structured prompt with context.</summary>
    Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default);

    /// <summary>Provider name for logging (e.g., "OpenAI-gpt-4o", "Ollama-codellama").</summary>
    string ProviderName { get; }

    /// <summary>Whether this provider is available and configured.</summary>
    bool IsAvailable { get; }
}

/// <summary>
/// Structured prompt sent to the LLM. Agents populate SystemPrompt with their
/// domain expertise and UserPrompt with the specific generation task.
/// </summary>
public sealed class LlmPrompt
{
    /// <summary>System-level instruction defining the agent's role and constraints.</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>The specific code generation request.</summary>
    public string UserPrompt { get; init; } = string.Empty;

    /// <summary>Optional — existing code context for the LLM to reference.</summary>
    public List<string> ContextSnippets { get; init; } = [];

    /// <summary>Temperature: 0.0 = deterministic, 0.7 = creative.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>Max tokens in the response.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>Requesting agent for telemetry.</summary>
    public string RequestingAgent { get; init; } = string.Empty;
}

/// <summary>Response from the LLM provider.</summary>
public sealed class LlmResponse
{
    public bool Success { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public TimeSpan Latency { get; init; }
    public string? Error { get; init; }
}
