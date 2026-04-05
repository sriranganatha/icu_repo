using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HmsAgents.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Llm;

/// <summary>
/// Calls OpenAI-compatible APIs (OpenAI, Azure OpenAI, Ollama, LM Studio, vLLM).
/// Config keys: Llm:Provider, Llm:ApiKey, Llm:Endpoint, Llm:Model.
/// Falls back gracefully when no API key is configured.
/// </summary>
public sealed class OpenAiLlmProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenAiLlmProvider> _logger;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly bool _available;

    public string ProviderName => $"OpenAI-{_model}";
    public bool IsAvailable => _available;

    public OpenAiLlmProvider(IConfiguration config, ILogger<OpenAiLlmProvider> logger)
    {
        _logger = logger;
        _model = config["Llm:Model"] ?? "gpt-4o";
        _endpoint = config["Llm:Endpoint"]?.TrimEnd('/') ?? "https://api.openai.com/v1";
        var apiKey = config["Llm:ApiKey"] ?? "";

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _available = true;
        }
        else
        {
            _available = false;
            _logger.LogWarning("LLM provider not configured (no Llm:ApiKey). Agents will use template fallback.");
        }
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        if (!_available)
        {
            return new LlmResponse
            {
                Success = false,
                Error = "LLM provider not configured. Set Llm:ApiKey in appsettings or environment.",
                Model = _model
            };
        }

        var sw = Stopwatch.StartNew();

        var messages = new List<object>
        {
            new { role = "system", content = prompt.SystemPrompt }
        };

        // Add context snippets as assistant messages for reference
        foreach (var snippet in prompt.ContextSnippets)
        {
            messages.Add(new { role = "assistant", content = $"[Context]\n{snippet}" });
        }

        messages.Add(new { role = "user", content = prompt.UserPrompt });

        var body = new
        {
            model = _model,
            messages,
            temperature = prompt.Temperature,
            max_tokens = prompt.MaxTokens
        };

        try
        {
            var json = JsonSerializer.Serialize(body);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_endpoint}/chat/completions", httpContent, ct);

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("LLM API error {Status}: {Body}", response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
                return new LlmResponse
                {
                    Success = false,
                    Error = $"API error: {response.StatusCode}",
                    Model = _model,
                    Latency = sw.Elapsed
                };
            }

            var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";

            _logger.LogInformation("LLM [{Agent}] → {Model} — {PromptTok}+{CompTok} tokens, {Ms}ms",
                prompt.RequestingAgent, _model,
                result?.Usage?.PromptTokens ?? 0,
                result?.Usage?.CompletionTokens ?? 0,
                sw.ElapsedMilliseconds);

            return new LlmResponse
            {
                Success = true,
                Content = content,
                Model = _model,
                PromptTokens = result?.Usage?.PromptTokens ?? 0,
                CompletionTokens = result?.Usage?.CompletionTokens ?? 0,
                Latency = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for {Agent}", prompt.RequestingAgent);
            return new LlmResponse
            {
                Success = false,
                Error = ex.Message,
                Model = _model,
                Latency = sw.Elapsed
            };
        }
    }

    public void Dispose() => _http.Dispose();

    // ─── JSON response models ───────────────────────────────────────────────

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public MessageContent? Message { get; set; }
    }

    private sealed class MessageContent
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}
