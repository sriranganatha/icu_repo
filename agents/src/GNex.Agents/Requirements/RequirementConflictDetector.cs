using GNex.Core.Enums;
using GNex.Core.Extensions;
using GNex.Core.Interfaces;
using GNex.Core.Models;

namespace GNex.Agents.Requirements;

/// <summary>
/// Detects duplicate and contradictory requirements using LLM-based semantic analysis
/// with a static Jaccard/opposition fallback when LLM is unavailable.
/// </summary>
public static class RequirementConflictDetector
{
    /// <summary>
    /// Scans a requirement set using LLM for semantic duplicate and contradiction detection.
    /// Falls back to static analysis if LLM is unavailable.
    /// </summary>
    public static async Task<List<ReviewFinding>> DetectAsync(
        List<Requirement> requirements, ILlmProvider? llm, AgentContext? context, CancellationToken ct = default)
    {
        if (requirements.Count < 2) return [];

        // Try LLM-based analysis first
        if (llm is not null && requirements.Count >= 2)
        {
            try
            {
                var llmFindings = await DetectWithLlmAsync(requirements, llm, context, ct);
                if (llmFindings.Count > 0)
                    return llmFindings;
            }
            catch
            {
                // Fall through to static analysis
            }
        }

        return Detect(requirements);
    }

    /// <summary>
    /// LLM-based semantic conflict detection — analyzes requirement pairs for duplicates,
    /// contradictions, and ambiguities using domain context.
    /// </summary>
    private static async Task<List<ReviewFinding>> DetectWithLlmAsync(
        List<Requirement> requirements, ILlmProvider llm, AgentContext? context, CancellationToken ct)
    {
        var reqSummary = string.Join("\n", requirements
            .Take(80)
            .Select(r => $"- [{r.Id}] {r.Title}: {r.Description}"));

        var llmContext = context is not null
            ? context.BuildLlmContextBlock(AgentType.RequirementsReader)
            : "";

        var prompt = new LlmPrompt
        {
            SystemPrompt = $$"""
                You are a senior business analyst expert at detecting requirement conflicts.
                Analyze the given requirements for:
                1. DUPLICATES: Requirements that express the same thing in different words (semantic similarity)
                2. CONTRADICTIONS: Requirements that conflict with each other (opposing constraints, conflicting behavior)
                3. AMBIGUITIES: Requirements that are vague enough to cause implementation confusion

                {{(!string.IsNullOrWhiteSpace(llmContext) ? llmContext : "")}}

                For each issue found, output a JSON array with objects:
                { "type": "duplicate|contradiction|ambiguity", "reqA": "REQ-ID-A", "reqB": "REQ-ID-B", "description": "explanation", "suggestion": "how to fix" }

                Output ONLY valid JSON array. Empty array [] if no issues found.
                """,
            UserPrompt = $"Analyze these requirements:\n\n{reqSummary}",
            Temperature = 0.2,
            MaxTokens = 3000,
            RequestingAgent = "RequirementConflictDetector"
        };

        var response = await llm.GenerateAsync(prompt, ct);
        if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            return [];

        return ParseConflictFindings(response.Content);
    }

    private static List<ReviewFinding> ParseConflictFindings(string json)
    {
        var findings = new List<ReviewFinding>();
        try
        {
            json = json.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..^3].Trim();

            var items = System.Text.Json.JsonSerializer.Deserialize<List<ConflictDto>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items is null) return findings;

            foreach (var item in items)
            {
                findings.Add(new ReviewFinding
                {
                    Category = item.Type?.ToLowerInvariant() switch
                    {
                        "duplicate" => "RequirementDuplicate",
                        "contradiction" => "RequirementContradiction",
                        "ambiguity" => "RequirementAmbiguity",
                        _ => "RequirementConflict"
                    },
                    Severity = item.Type?.ToLowerInvariant() switch
                    {
                        "contradiction" => ReviewSeverity.Error,
                        "duplicate" => ReviewSeverity.Warning,
                        _ => ReviewSeverity.Info
                    },
                    Message = $"Requirements {item.ReqA} and {item.ReqB}: {item.Description}",
                    FilePath = "docs/requirements",
                    Suggestion = item.Suggestion ?? "Review and resolve the conflict."
                });
            }
        }
        catch { /* fallback will handle */ }
        return findings;
    }

    private sealed class ConflictDto
    {
        public string? Type { get; set; }
        public string? ReqA { get; set; }
        public string? ReqB { get; set; }
        public string? Description { get; set; }
        public string? Suggestion { get; set; }
    }

    /// <summary>
    /// Static fallback: scans a requirement set using Jaccard similarity and opposition words.
    /// </summary>
    public static List<ReviewFinding> Detect(List<Requirement> requirements)
    {
        var findings = new List<ReviewFinding>();
        if (requirements.Count < 2) return findings;

        for (var i = 0; i < requirements.Count; i++)
        {
            for (var j = i + 1; j < requirements.Count; j++)
            {
                var a = requirements[i];
                var b = requirements[j];

                var similarity = ComputeSimilarity(a, b);

                if (similarity >= 0.85)
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "RequirementDuplicate",
                        Severity = ReviewSeverity.Warning,
                        Message = $"Requirements {a.Id} and {b.Id} appear to be duplicates (similarity: {similarity:P0}). Consider merging.",
                        FilePath = "docs/requirements",
                        Suggestion = $"Merge '{a.Title}' and '{b.Title}' into a single requirement."
                    });
                }
                else if (similarity >= 0.5)
                {
                    var contradiction = DetectContradiction(a, b);
                    if (contradiction is not null)
                    {
                        findings.Add(new ReviewFinding
                        {
                            Category = "RequirementContradiction",
                            Severity = ReviewSeverity.Error,
                            Message = $"Requirements {a.Id} and {b.Id} may contradict each other: {contradiction}",
                            FilePath = "docs/requirements",
                            Suggestion = "Resolve the contradiction by clarifying scope or constraints before implementation."
                        });
                    }
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Computes text similarity using Jaccard coefficient on word-level tokens.
    /// </summary>
    internal static double ComputeSimilarity(Requirement a, Requirement b)
    {
        var tokensA = Tokenize($"{a.Title} {a.Description}");
        var tokensB = Tokenize($"{b.Title} {b.Description}");

        if (tokensA.Count == 0 && tokensB.Count == 0) return 1.0;
        if (tokensA.Count == 0 || tokensB.Count == 0) return 0.0;

        var intersection = tokensA.Intersect(tokensB, StringComparer.OrdinalIgnoreCase).Count();
        var union = tokensA.Union(tokensB, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// Detects contradictions by checking for opposing constraint language in similar requirements.
    /// </summary>
    internal static string? DetectContradiction(Requirement a, Requirement b)
    {
        var textA = $"{a.Title} {a.Description}".ToLowerInvariant();
        var textB = $"{b.Title} {b.Description}".ToLowerInvariant();

        // Check opposing patterns
        var oppositions = new (string positive, string negative, string description)[]
        {
            ("must", "must not", "conflicting obligation"),
            ("shall", "shall not", "conflicting mandate"),
            ("allow", "deny", "conflicting access rule"),
            ("allow", "block", "conflicting access rule"),
            ("enable", "disable", "conflicting feature toggle"),
            ("required", "optional", "conflicting requirement level"),
            ("synchronous", "asynchronous", "conflicting execution model"),
            ("real-time", "batch", "conflicting processing model"),
            ("encrypt", "plaintext", "conflicting data handling"),
        };

        foreach (var (pos, neg, desc) in oppositions)
        {
            if ((textA.Contains(pos) && textB.Contains(neg)) ||
                (textA.Contains(neg) && textB.Contains(pos)))
            {
                // Only flag if they're about a common subject
                var commonSubject = FindCommonSubject(a, b);
                if (commonSubject is not null)
                    return $"{desc} on '{commonSubject}'";
            }
        }

        return null;
    }

    private static string? FindCommonSubject(Requirement a, Requirement b)
    {
        var tokensA = Tokenize(a.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokensB = Tokenize(b.Title);

        var common = tokensB
            .Where(t => tokensA.Contains(t) && t.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return common.Count > 0 ? string.Join(" ", common) : null;
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .Split([' ', '\t', '\n', '\r', ',', '.', ';', ':', '(', ')', '-', '/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
