using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Refactoring;

/// <summary>
/// Refactoring agent — analyzes Review and CodeQuality findings, applies AI-driven
/// refactoring: dead code removal, pattern application (Repository, CQRS, Strategy),
/// DI optimization, code smell elimination, and SOLID principle enforcement.
/// </summary>
public sealed class RefactoringAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<RefactoringAgent> _logger;

    public AgentType Type => AgentType.Refactoring;
    public string Name => "Refactoring Agent";
    public string Description => "AI-driven refactoring — dead code removal, pattern application, SOLID enforcement.";

    public RefactoringAgent(ILlmProvider llm, ILogger<RefactoringAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("RefactoringAgent starting — {FindingCount} findings to process", context.Findings.Count);

        var artifacts = new List<CodeArtifact>();

        try
        {
            // Gather refactoring-relevant findings
            var qualityFindings = context.Findings
                .Where(f => f.Category.StartsWith("CodeQuality") || f.Category == "Implementation" || f.Category == "Audit")
                .GroupBy(f => f.FilePath)
                .ToList();

            // Also scan for dead code patterns
            var outputPath = context.OutputBasePath;
            var csFiles = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj") && !f.Contains("bin")).ToArray()
                : [];

            // ── Dead code detection ──
            var unusedUsings = new List<(string File, string Using)>();
            var emptyMethods = new List<(string File, int Line)>();
            foreach (var file in csFiles)
            {
                ct.ThrowIfCancellationRequested();
                var content = await File.ReadAllTextAsync(file, ct);
                var relPath = Path.GetRelativePath(outputPath, file);

                // Find unused using directives (simple heuristic)
                var usings = System.Text.RegularExpressions.Regex.Matches(content, @"^using\s+([\w.]+);", System.Text.RegularExpressions.RegexOptions.Multiline);
                foreach (System.Text.RegularExpressions.Match u in usings)
                {
                    var ns = u.Groups[1].Value;
                    var lastPart = ns.Split('.').Last();
                    // Heuristic: if the last part of the namespace never appears elsewhere in the file
                    var bodyStart = content.IndexOf('{');
                    if (bodyStart > 0)
                    {
                        var body = content[bodyStart..];
                        if (!body.Contains(lastPart))
                            unusedUsings.Add((relPath, ns));
                    }
                }

                // Find empty method bodies
                var emptyBodies = System.Text.RegularExpressions.Regex.Matches(content,
                    @"(public|private|protected)\s+\w+\s+\w+\s*\([^)]*\)\s*\{\s*\}");
                foreach (System.Text.RegularExpressions.Match m in emptyBodies)
                {
                    var line = content[..m.Index].Count(c => c == '\n') + 1;
                    emptyMethods.Add((relPath, line));
                }
            }

            // ── AI refactoring per file group ──
            var refactoredFiles = 0;
            foreach (var group in qualityFindings.Take(15))
            {
                ct.ThrowIfCancellationRequested();
                var filePath = Path.Combine(outputPath, group.Key);
                if (!File.Exists(filePath)) continue;

                var content = await File.ReadAllTextAsync(filePath, ct);
                var findingBlock = string.Join("\n", group.Select(f =>
                    $"Line {f.LineNumber}: [{f.Category}] {f.Message} — {f.Suggestion}"));

                var prompt = $"""
                    You are a senior .NET 8 architect. Refactor this C# file to address the findings below.
                    Apply SOLID principles, extract methods, reduce complexity, fix naming.
                    Return ONLY the complete refactored C# file with no explanations.

                    Findings:
                    {findingBlock}

                    Current code:
                    ```csharp
                    {Truncate(content, 8000)}
                    ```
                    """;

                try
                {
                    var refactored = await _llm.GenerateAsync(prompt, ct);
                    // Strip markdown fences if present
                    refactored = refactored
                        .Replace("```csharp", "").Replace("```cs", "").Replace("```", "").Trim();

                    if (refactored.Length > 100)
                    {
                        artifacts.Add(new CodeArtifact
                        {
                            Layer = ArtifactLayer.Service,
                            RelativePath = group.Key,
                            FileName = Path.GetFileName(group.Key),
                            Namespace = string.Empty,
                            ProducedBy = Type,
                            TracedRequirementIds = ["NFR-REFACTOR-01"],
                            Content = refactored
                        });
                        refactoredFiles++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refactor {File}", group.Key);
                }
            }

            // ── Refactoring summary artifact ──
            var summaryBuilder = new System.Text.StringBuilder();
            summaryBuilder.AppendLine("# Refactoring Report");
            summaryBuilder.AppendLine($"- Files refactored: {refactoredFiles}");
            summaryBuilder.AppendLine($"- Unused usings detected: {unusedUsings.Count}");
            summaryBuilder.AppendLine($"- Empty methods detected: {emptyMethods.Count}");
            summaryBuilder.AppendLine($"- Quality findings processed: {qualityFindings.Sum(g => g.Count())}");
            summaryBuilder.AppendLine($"- Duration: {sw.Elapsed.TotalSeconds:F1}s");

            if (unusedUsings.Count > 0)
            {
                summaryBuilder.AppendLine("\n## Unused Usings");
                foreach (var (file, ns) in unusedUsings.Take(30))
                    summaryBuilder.AppendLine($"- `{file}`: `using {ns};`");
            }

            if (emptyMethods.Count > 0)
            {
                summaryBuilder.AppendLine("\n## Empty Methods");
                foreach (var (file, line) in emptyMethods.Take(20))
                    summaryBuilder.AppendLine($"- `{file}` line {line}");
            }

            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "quality/refactoring-report.md",
                FileName = "refactoring-report.md",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-REFACTOR-02"],
                Content = summaryBuilder.ToString()
            });

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Refactoring Agent: {refactoredFiles} files refactored, {unusedUsings.Count} unused usings, {emptyMethods.Count} empty methods",
                Artifacts = artifacts, Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "RefactoringAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
