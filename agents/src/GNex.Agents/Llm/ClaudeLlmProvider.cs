using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GNex.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Llm;

/// <summary>
/// Calls Anthropic Claude Messages API (api.anthropic.com).
/// Config keys: Claude:ApiKey, Claude:Model (default claude-sonnet-4-20250514), Claude:Endpoint.
/// </summary>
public sealed class ClaudeLlmProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<ClaudeLlmProvider> _logger;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly bool _available;

    public string ProviderName => $"Claude-{_model}";
    public bool IsAvailable => _available;

    public ClaudeLlmProvider(IConfiguration config, ILogger<ClaudeLlmProvider> logger)
    {
        _logger = logger;
        _model = config["Claude:Model"] ?? "claude-opus-4-20250514";
        _apiKey = config["Claude:ApiKey"] ?? "";
        _endpoint = config["Claude:Endpoint"]?.TrimEnd('/')
            ?? "https://api.anthropic.com/v1";

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _available = !string.IsNullOrEmpty(_apiKey);

        if (!_available)
            _logger.LogWarning("Claude LLM provider not configured (no Claude:ApiKey). Will fall back to other providers.");
    }

    public async Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        if (!_available)
        {
            return new LlmResponse
            {
                Success = false,
                Error = "Claude provider not configured. Set Claude:ApiKey in appsettings or environment.",
                Model = _model
            };
        }

        var sw = Stopwatch.StartNew();

        // Build user content: context snippets + user prompt
        var userContent = new StringBuilder();
        foreach (var snippet in prompt.ContextSnippets)
        {
            userContent.AppendLine($"[Context]\n{snippet}\n");
        }
        userContent.Append(prompt.UserPrompt);

        var body = new
        {
            model = _model,
            max_tokens = prompt.MaxTokens,
            system = prompt.SystemPrompt,
            temperature = prompt.Temperature,
            messages = new[]
            {
                new { role = "user", content = userContent.ToString() }
            }
        };

        try
        {
            var json = JsonSerializer.Serialize(body);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/messages")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // Claude uses x-api-key header + anthropic-version header
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Claude API error {Status}: {Body}",
                    response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
                return new LlmResponse
                {
                    Success = false,
                    Error = $"Claude API error: {response.StatusCode}",
                    Model = _model,
                    Latency = sw.Elapsed
                };
            }

            var result = JsonSerializer.Deserialize<ClaudeResponse>(responseBody);
            var content = result?.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? "";

            var promptTokens = result?.Usage?.InputTokens ?? 0;
            var completionTokens = result?.Usage?.OutputTokens ?? 0;

            _logger.LogInformation("Claude [{Agent}] → {Model} — {PromptTok}+{CompTok} tokens, {Ms}ms",
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
            _logger.LogError(ex, "Claude call failed for {Agent}", prompt.RequestingAgent);
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

    // ─── Claude JSON response models ────────────────────────────────────────

    private sealed class ClaudeResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public List<ClaudeContentBlock>? Content { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public ClaudeUsage? Usage { get; set; }
    }

    private sealed class ClaudeContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class ClaudeUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
}
