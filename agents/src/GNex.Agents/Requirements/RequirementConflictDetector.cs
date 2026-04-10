using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Agents.Requirements;

/// <summary>
/// Detects duplicate and contradictory requirements using text similarity
/// and semantic heuristics. Flags issues and suggests merges.
/// </summary>
public static class RequirementConflictDetector
{
    /// <summary>
    /// Scans a requirement set and returns findings for duplicates and contradictions.
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
