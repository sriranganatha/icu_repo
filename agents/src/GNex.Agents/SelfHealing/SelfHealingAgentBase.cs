using System.Diagnostics;
using System.Text;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.SelfHealing;

/// <summary>
/// Base class for agents with built-in self-healing capabilities.
/// Provides: internal retry loop, AI root-cause analysis on failure,
/// automated fix attempts, healing report generation, and graceful degradation.
/// 
/// Derived agents implement <see cref="ExecuteCoreAsync"/> with their main logic.
/// The base handles retries, error diagnosis, and recovery.
/// </summary>
public abstract class SelfHealingAgentBase : IAgent
{
    protected readonly ILlmProvider Llm;
    protected readonly ILogger Logger;

    private const int MaxInternalRetries = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    public abstract AgentType Type { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    protected SelfHealingAgentBase(ILlmProvider llm, ILogger logger)
    {
        Llm = llm;
        Logger = logger;
    }

    /// <summary>
    /// Core agent logic — implemented by derived agents.
    /// Throw on failure; the base will catch, diagnose, and retry.
    /// </summary>
    protected abstract Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct);

    /// <summary>
    /// Optional: derived agents can override to apply a specific fix based on the diagnosis.
    /// Return true if a fix was applied and retry should proceed.
    /// </summary>
    protected virtual Task<bool> ApplyFixAsync(AgentContext context, string error, string diagnosis, int attempt, CancellationToken ct)
        => Task.FromResult(false);

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        var healingLog = new List<HealingEntry>();
        AgentResult? lastResult = null;

        for (int attempt = 1; attempt <= MaxInternalRetries; attempt++)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (attempt > 1)
                {
                    Logger.LogWarning("[SelfHeal] {Agent} internal retry {Attempt}/{Max}", Name, attempt, MaxInternalRetries);
                    context.AgentStatuses[Type] = AgentStatus.Running;
                    await Task.Delay(RetryBaseDelay * attempt, ct);
                }

                lastResult = await ExecuteCoreAsync(context, ct);

                if (lastResult.Success)
                {
                    // Attach healing report if any retries occurred
                    if (healingLog.Count > 0)
                    {
                        var healReport = BuildHealingReport(healingLog, sw.Elapsed, success: true);
                        lastResult = new AgentResult
                        {
                            Agent = lastResult.Agent,
                            Success = true,
                            Summary = $"{lastResult.Summary} [self-healed after {attempt - 1} retries]",
                            Artifacts = [..lastResult.Artifacts, healReport],
                            Findings = lastResult.Findings,
                            Messages = lastResult.Messages,
                            Errors = lastResult.Errors,
                            Duration = lastResult.Duration
                        };
                        context.Artifacts.Add(healReport);
                    }
                    context.AgentStatuses[Type] = AgentStatus.Completed;

                    // Agent completes its own claimed work items
                    foreach (var item in context.CurrentClaimedItems)
                        context.CompleteWorkItem?.Invoke(item);

                    return lastResult;
                }

                // Agent returned Success=false — treat as recoverable failure
                var errorMsg = string.Join("; ", lastResult.Errors.Take(3));
                if (string.IsNullOrEmpty(errorMsg)) errorMsg = lastResult.Summary;

                Logger.LogWarning("[SelfHeal] {Agent} returned failure: {Error}", Name, errorMsg);
                var entry = await DiagnoseAndHeal(context, errorMsg, attempt, ct);
                healingLog.Add(entry);

                if (!entry.FixApplied)
                {
                    Logger.LogWarning("[SelfHeal] {Agent} — no fix available, will retry with adjustments", Name);
                }
            }
            catch (OperationCanceledException)
            {
                context.AgentStatuses[Type] = AgentStatus.Failed;
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SelfHeal] {Agent} threw exception on attempt {Attempt}", Name, attempt);
                var entry = await DiagnoseAndHeal(context, ex.Message, attempt, ct);
                healingLog.Add(entry);

                if (attempt == MaxInternalRetries)
                {
                    // Final attempt failed — return failure with healing report
                    var healReport = BuildHealingReport(healingLog, sw.Elapsed, success: false);
                    context.Artifacts.Add(healReport);
                    context.AgentStatuses[Type] = AgentStatus.Failed;

                    return new AgentResult
                    {
                        Agent = Type,
                        Success = false,
                        Summary = $"{Name}: Failed after {MaxInternalRetries} self-heal attempts — {ex.Message}",
                        Errors = [ex.Message, ..healingLog.Select(h => $"Attempt {h.Attempt}: {h.Error} → {h.Diagnosis}")],
                        Artifacts = [healReport],
                        Duration = sw.Elapsed
                    };
                }
            }
        }

        // Exhausted retries but never threw — return last result with healing report
        var finalReport = BuildHealingReport(healingLog, sw.Elapsed, success: false);
        context.Artifacts.Add(finalReport);
        context.AgentStatuses[Type] = AgentStatus.Failed;

        return lastResult ?? new AgentResult
        {
            Agent = Type,
            Success = false,
            Summary = $"{Name}: Exhausted {MaxInternalRetries} self-heal attempts",
            Errors = healingLog.Select(h => $"Attempt {h.Attempt}: {h.Error}").ToList(),
            Artifacts = [finalReport],
            Duration = sw.Elapsed
        };
    }

    private async Task<HealingEntry> DiagnoseAndHeal(AgentContext context, string error, int attempt, CancellationToken ct)
    {
        var entry = new HealingEntry
        {
            Attempt = attempt,
            Error = Truncate(error, 500),
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // AI root-cause analysis
            var diagPrompt = new LlmPrompt
            {
                SystemPrompt = $"""
                    You are a {context.SeniorRoleLabel("developer and DevOps engineer")} specializing in enterprise systems.
                    Analyze the error from the {Name} and provide:
                    1. ROOT CAUSE: One-line explanation
                    2. FIX: Specific actionable fix (code change, config change, or command)
                    3. PREVENTION: How to prevent this in future
                    Keep each section to 1-2 sentences.
                    """,
                UserPrompt = $"""
                    Agent: {Name} (attempt {attempt}/{MaxInternalRetries})
                    Error: {error}
                    Context: Output path = {context.OutputBasePath}, Artifacts so far = {context.Artifacts.Count}, Findings = {context.Findings.Count}
                    """,
                Temperature = 0.1,
                MaxTokens = 500,
                RequestingAgent = $"{Name}-SelfHeal"
            };

            var response = await Llm.GenerateAsync(diagPrompt, ct);
            if (response.Success)
            {
                entry.Diagnosis = response.Content;
                Logger.LogInformation("[SelfHeal] {Agent} diagnosis: {Diagnosis}", Name, Truncate(response.Content, 200));
            }
            else
            {
                entry.Diagnosis = $"AI diagnosis unavailable: {response.Error}";
            }

            // Attempt automated fix
            entry.FixApplied = await ApplyFixAsync(context, error, entry.Diagnosis, attempt, ct);
            if (entry.FixApplied)
                Logger.LogInformation("[SelfHeal] {Agent} — fix applied on attempt {Attempt}", Name, attempt);
        }
        catch (Exception ex)
        {
            entry.Diagnosis = $"Diagnosis failed: {ex.Message}";
            Logger.LogWarning(ex, "[SelfHeal] {Agent} diagnosis error", Name);
        }

        return entry;
    }

    private CodeArtifact BuildHealingReport(List<HealingEntry> log, TimeSpan elapsed, bool success)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Self-Healing Report: {Name}");
        sb.AppendLine($"**Outcome**: {(success ? "RECOVERED" : "FAILED")}");
        sb.AppendLine($"**Attempts**: {log.Count}");
        sb.AppendLine($"**Duration**: {elapsed.TotalSeconds:F1}s");
        sb.AppendLine();

        foreach (var entry in log)
        {
            sb.AppendLine($"## Attempt {entry.Attempt} — {entry.Timestamp:HH:mm:ss}");
            sb.AppendLine($"**Error**: {entry.Error}");
            sb.AppendLine($"**Diagnosis**: {entry.Diagnosis}");
            sb.AppendLine($"**Fix Applied**: {entry.FixApplied}");
            sb.AppendLine();
        }

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = $"healing/{Type.ToString().ToLowerInvariant()}-healing-report.md",
            FileName = $"{Type.ToString().ToLowerInvariant()}-healing-report.md",
            Namespace = string.Empty,
            ProducedBy = Type,
            TracedRequirementIds = ["NFR-SELFHEAL-01"],
            Content = sb.ToString()
        };
    }

    /// <summary>Helper to call LLM with a simple prompt string.</summary>
    protected async Task<string> AskLlmAsync(string prompt, CancellationToken ct, double temp = 0.2, int maxTokens = 4096, AgentContext? context = null)
    {
        var roleLabel = context is not null ? context.ExpertRoleLabel() : "expert .NET/C# developer";
        var response = await Llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = $"You are an {roleLabel} for an enterprise application. Agent: {Name}.",
            UserPrompt = prompt,
            Temperature = temp,
            MaxTokens = maxTokens,
            RequestingAgent = Name
        }, ct);

        return response.Success ? response.Content : throw new InvalidOperationException($"LLM failed: {response.Error}");
    }

    /// <summary>Helper to call LLM, returning empty string instead of throwing on failure.</summary>
    protected async Task<string> TryAskLlmAsync(string prompt, CancellationToken ct, double temp = 0.2, int maxTokens = 4096, AgentContext? context = null)
    {
        var roleLabel = context is not null ? context.ExpertRoleLabel() : "expert .NET/C# developer";
        var response = await Llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = $"You are an {roleLabel} for an enterprise application. Agent: {Name}.",
            UserPrompt = prompt,
            Temperature = temp,
            MaxTokens = maxTokens,
            RequestingAgent = Name
        }, ct);

        return response.Success ? response.Content : string.Empty;
    }

    protected static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    protected static string StripCodeFences(string s) =>
        s.Replace("```csharp", "").Replace("```cs", "").Replace("```json", "")
         .Replace("```html", "").Replace("```cshtml", "").Replace("```razor", "")
         .Replace("```javascript", "").Replace("```js", "").Replace("```sql", "")
         .Replace("```css", "").Replace("```yaml", "").Replace("```xml", "")
         .Replace("```", "").Trim();

    private sealed class HealingEntry
    {
        public int Attempt { get; init; }
        public string Error { get; set; } = string.Empty;
        public string Diagnosis { get; set; } = "Pending";
        public bool FixApplied { get; set; }
        public DateTime Timestamp { get; init; }
    }
}
