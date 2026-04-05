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
/// Calls Google Gemini API (generativelanguage.googleapis.com).
/// Config keys: Llm:ApiKey (Gemini API key), Llm:Model (default gemini-2.0-flash).
/// Supports all Gemini models: gemini-2.0-flash, gemini-2.5-pro, etc.
/// </summary>
public sealed class GeminiLlmProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<GeminiLlmProvider> _logger;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly bool _available;

    public string ProviderName => $"Gemini-{_model}";
    public bool IsAvailable => _available;

    public GeminiLlmProvider(IConfiguration config, ILogger<GeminiLlmProvider> logger)
    {
        _logger = logger;
        _model = config["Llm:Model"] ?? "gemini-2.0-flash";
        _apiKey = config["Llm:ApiKey"] ?? "";
        _endpoint = config["Llm:Endpoint"]?.TrimEnd('/')
            ?? "https://generativelanguage.googleapis.com/v1beta";

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _available = !string.IsNullOrEmpty(_apiKey);

        if (!_available)
            _logger.LogWarning("Gemini LLM provider not configured (no Llm:ApiKey). Agents will use template fallback.");
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        if (!_available)
        {
            return new LlmResponse
            {
                Success = false,
                Error = "Gemini provider not configured. Set Llm:ApiKey in appsettings or environment.",
                Model = _model
            };
        }

        var sw = Stopwatch.StartNew();

        // Build Gemini request: system instruction + user content parts
        var contents = new List<object>();

        // Context snippets as user context
        var userParts = new List<object>();
        foreach (var snippet in prompt.ContextSnippets)
        {
            userParts.Add(new { text = $"[Context]\n{snippet}" });
        }
        userParts.Add(new { text = prompt.UserPrompt });

        contents.Add(new { role = "user", parts = userParts });

        var body = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = prompt.SystemPrompt } }
            },
            contents,
            generationConfig = new
            {
                temperature = prompt.Temperature,
                maxOutputTokens = prompt.MaxTokens,
            }
        };

        try
        {
            var json = JsonSerializer.Serialize(body);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            // Gemini uses API key as query parameter
            var url = $"{_endpoint}/models/{_model}:generateContent?key={_apiKey}";
            var response = await _http.PostAsync(url, httpContent, ct);

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error {Status}: {Body}",
                    response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
                return new LlmResponse
                {
                    Success = false,
                    Error = $"Gemini API error: {response.StatusCode}",
                    Model = _model,
                    Latency = sw.Elapsed
                };
            }

            var result = JsonSerializer.Deserialize<GeminiResponse>(responseBody);
            var content = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";

            var promptTokens = result?.UsageMetadata?.PromptTokenCount ?? 0;
            var completionTokens = result?.UsageMetadata?.CandidatesTokenCount ?? 0;

            _logger.LogInformation("Gemini [{Agent}] → {Model} — {PromptTok}+{CompTok} tokens, {Ms}ms",
                prompt.RequestingAgent, _model,
                promptTokens, completionTokens, sw.ElapsedMilliseconds);

            return new LlmResponse
            {
                Success = true,
                Content = content,
                Model = _model,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                Latency = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini call failed for {Agent}", prompt.RequestingAgent);
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

    // ─── Gemini JSON response models ────────────────────────────────────────

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }
}
