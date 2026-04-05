using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.CodeQuality;

/// <summary>
/// Code quality agent — performs static analysis on generated code: cyclomatic complexity,
/// duplication detection, naming conventions, method length, class cohesion, and coding
/// standards enforcement. Produces structured findings and a quality dashboard artifact.
/// </summary>
public sealed class CodeQualityAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<CodeQualityAgent> _logger;

    public AgentType Type => AgentType.CodeQuality;
    public string Name => "Code Quality Agent";
    public string Description => "Static analysis — cyclomatic complexity, duplication, naming conventions, coding standards.";

    public CodeQualityAgent(ILlmProvider llm, ILogger<CodeQualityAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("CodeQualityAgent starting");

        var findings = new List<ReviewFinding>();
        var artifacts = new List<CodeArtifact>();
        var report = new StringBuilder();
        report.AppendLine("# Code Quality Report");
        report.AppendLine($"**Generated**: {DateTime.UtcNow:u}");
        report.AppendLine();

        try
        {
            var outputPath = context.OutputBasePath;
            var csFiles = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj") && !f.Contains("bin") && !f.Contains("Migrations")).ToArray()
                : [];

            report.AppendLine($"## Scope: {csFiles.Length} C# files");
            report.AppendLine();

            int totalMethods = 0, longMethods = 0, longClasses = 0, namingIssues = 0;
            var fileMetrics = new List<(string File, int Lines, int Methods, int Complexity)>();

            foreach (var file in csFiles)
            {
                ct.ThrowIfCancellationRequested();
                var content = await File.ReadAllTextAsync(file, ct);
                var lines = content.Split('\n');
                var relPath = Path.GetRelativePath(outputPath, file);

                // ── Method count & length ──
                var methodMatches = Regex.Matches(content, @"(public|private|protected|internal)\s+(static\s+)?(async\s+)?\w+[\w<>\[\],\s]*\s+\w+\s*\(");
                totalMethods += methodMatches.Count;

                foreach (Match mm in methodMatches)
                {
                    var startLine = content[..mm.Index].Count(c => c == '\n') + 1;
                    var braceDepth = 0;
                    var methodLines = 0;
                    var foundOpen = false;
                    for (var i = startLine - 1; i < lines.Length; i++)
                    {
                        if (lines[i].Contains('{')) { braceDepth++; foundOpen = true; }
                        if (lines[i].Contains('}')) braceDepth--;
                        if (foundOpen) methodLines++;
                        if (foundOpen && braceDepth == 0) break;
                    }

                    if (methodLines > 50)
                    {
                        longMethods++;
                        findings.Add(new ReviewFinding
                        {
                            FilePath = relPath, LineNumber = startLine,
                            Severity = methodLines > 100 ? ReviewSeverity.Warning : ReviewSeverity.Info,
                            Category = "CodeQuality-MethodLength",
                            Message = $"Method at line {startLine} is {methodLines} lines (recommended: <50).",
                            Suggestion = "Extract sub-methods or simplify logic."
                        });
                    }
                }

                // ── Class length ──
                if (lines.Length > 500)
                {
                    longClasses++;
                    findings.Add(new ReviewFinding
                    {
                        FilePath = relPath, LineNumber = 1,
                        Severity = ReviewSeverity.Warning,
                        Category = "CodeQuality-ClassLength",
                        Message = $"File has {lines.Length} lines (recommended: <500).",
                        Suggestion = "Split into partial classes or extract responsibilities."
                    });
                }

                // ── Naming conventions ──
                var badNames = Regex.Matches(content, @"public\s+\w+\s+([a-z]\w*)\s*\{");
                foreach (Match bn in badNames)
                {
                    if (bn.Groups[1].Value is "get" or "set" or "value") continue;
                    namingIssues++;
                    var line = content[..bn.Index].Count(c => c == '\n') + 1;
                    findings.Add(new ReviewFinding
                    {
                        FilePath = relPath, LineNumber = line,
                        Severity = ReviewSeverity.Info,
                        Category = "CodeQuality-Naming",
                        Message = $"Property '{bn.Groups[1].Value}' should use PascalCase.",
                        Suggestion = $"Rename to '{char.ToUpper(bn.Groups[1].Value[0])}{bn.Groups[1].Value[1..]}'."
                    });
                }

                // ── Cyclomatic complexity estimate ──
                var complexity = Regex.Matches(content, @"\b(if|else|while|for|foreach|case|catch|\?\?|&&|\|\|)\b").Count;
                fileMetrics.Add((relPath, lines.Length, methodMatches.Count, complexity));
            }

            // ── Duplication detection (simple hash-based) ──
            report.AppendLine("## Duplication Analysis");
            var duplicates = DetectDuplicates(csFiles, outputPath);
            report.AppendLine($"- Potential duplicate blocks: {duplicates.Count}");
            foreach (var dup in duplicates.Take(10))
            {
                findings.Add(new ReviewFinding
                {
                    FilePath = dup.FileA, LineNumber = dup.LineA,
                    Severity = ReviewSeverity.Info,
                    Category = "CodeQuality-Duplication",
                    Message = $"Duplicate code block (~{dup.Lines} lines) also found in {dup.FileB}:{dup.LineB}.",
                    Suggestion = "Extract shared logic into a common helper or base class."
                });
            }
            report.AppendLine();

            // ── AI quality summary ──
            report.AppendLine("## AI Quality Assessment");
            var topFiles = fileMetrics.OrderByDescending(f => f.Complexity).Take(5);
            var summaryPrompt = $"""
                Analyze this .NET 8 HMS codebase quality summary and provide 5 actionable recommendations:
                - Total files: {csFiles.Length}, Total methods: {totalMethods}
                - Long methods (>50 lines): {longMethods}
                - Long classes (>500 lines): {longClasses}
                - Naming issues: {namingIssues}
                - Duplicate blocks: {duplicates.Count}
                - Top complex files: {string.Join(", ", topFiles.Select(f => $"{f.File}(complexity:{f.Complexity})"))}
                Focus on maintainability, testability, and SOLID principles.
                """;
            try
            {
                var aiSummary = await _llm.GenerateAsync(summaryPrompt, ct);
                report.AppendLine(aiSummary);
            }
            catch { report.AppendLine("- AI assessment unavailable."); }
            report.AppendLine();

            // ── Metrics summary ──
            report.AppendLine("## Metrics Summary");
            report.AppendLine($"| Metric | Value |");
            report.AppendLine($"|--------|-------|");
            report.AppendLine($"| Files analyzed | {csFiles.Length} |");
            report.AppendLine($"| Total methods | {totalMethods} |");
            report.AppendLine($"| Long methods (>50 lines) | {longMethods} |");
            report.AppendLine($"| Large files (>500 lines) | {longClasses} |");
            report.AppendLine($"| Naming issues | {namingIssues} |");
            report.AppendLine($"| Duplicate blocks | {duplicates.Count} |");
            report.AppendLine($"| Total findings | {findings.Count} |");
            report.AppendLine($"| Duration | {sw.Elapsed.TotalSeconds:F1}s |");

            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "quality/code-quality-report.md",
                FileName = "code-quality-report.md",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-QUALITY-01"],
                Content = report.ToString()
            });

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Code Quality: {csFiles.Length} files, {findings.Count} findings, {longMethods} long methods, {duplicates.Count} duplicates",
                Artifacts = artifacts, Findings = findings, Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "CodeQualityAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private record DuplicateBlock(string FileA, int LineA, string FileB, int LineB, int Lines);

    private static List<DuplicateBlock> DetectDuplicates(string[] files, string basePath)
    {
        var results = new List<DuplicateBlock>();
        const int windowSize = 6;
        var hashMap = new Dictionary<int, (string File, int Line)>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file)
                .Select(l => l.Trim())
                .Where(l => l.Length > 5 && !l.StartsWith("//") && !l.StartsWith("using ") && l != "{" && l != "}")
                .ToArray();
            var relPath = Path.GetRelativePath(basePath, file);

            for (var i = 0; i <= lines.Length - windowSize; i++)
            {
                var window = string.Join("\n", lines.Skip(i).Take(windowSize));
                var hash = window.GetHashCode();
                if (hashMap.TryGetValue(hash, out var existing) && existing.File != relPath)
                {
                    results.Add(new DuplicateBlock(relPath, i + 1, existing.File, existing.Line, windowSize));
                    if (results.Count >= 50) return results;
                }
                else
                {
                    hashMap[hash] = (relPath, i + 1);
                }
            }
        }
        return results;
    }
}
