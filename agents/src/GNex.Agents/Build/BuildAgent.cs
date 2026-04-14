using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Build;

/// <summary>
/// Build agent — restores NuGet packages, compiles the generated solution,
/// runs dotnet test, parses MSBuild output for errors/warnings, and produces
/// a structured build report with per-project pass/fail status.
/// </summary>
public sealed class BuildAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<BuildAgent> _logger;

    public AgentType Type => AgentType.Build;
    public string Name => "Build Agent";
    public string Description => "Restores, compiles, and validates the generated solution — reports errors with AI-suggested fixes.";

    public BuildAgent(ILlmProvider llm, ILogger<BuildAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("BuildAgent starting for {Output}", context.OutputBasePath);

        var artifacts = new List<CodeArtifact>();
        var findings = new List<ReviewFinding>();
        var errors = new List<string>();
        var report = new StringBuilder();
        report.AppendLine("# Build Report");
        report.AppendLine($"**Output**: `{context.OutputBasePath}`");
        report.AppendLine($"**Time**: {DateTime.UtcNow:u}");
        report.AppendLine();

        try
        {
            // Read feedback from other agents (Refactoring writes to Build)
            var feedback = context.ReadFeedback(Type);
            if (feedback.Count > 0)
                _logger.LogInformation("BuildAgent received {Count} feedback items from Refactoring/other agents", feedback.Count);

            // Read upstream agent results for targeted rebuild awareness
            if (context.AgentResults.TryGetValue(AgentType.Refactoring, out var refactorResult) && refactorResult.Success)
                _logger.LogInformation("BuildAgent consuming Refactoring results: {Summary}", refactorResult.Summary);
            if (context.AgentResults.TryGetValue(AgentType.BugFix, out var bugFixResult) && bugFixResult.Success)
                _logger.LogInformation("BuildAgent consuming BugFix results: {Summary}", bugFixResult.Summary);

            var outputPath = context.OutputBasePath;
            if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
            {
                context.AgentStatuses[Type] = AgentStatus.Failed;
                return Fail("Output path not found", [$"Path does not exist: {outputPath}"], sw.Elapsed);
            }

            // ── Step 1: Find solution/projects ──
            var discoveryRoots = BuildDiscoveryRoots(outputPath, context);
            var slnFiles = EnumerateBuildFiles(discoveryRoots, "*.sln").ToArray();
            var csprojFiles = EnumerateBuildFiles(discoveryRoots, "*.csproj").ToArray();
            report.AppendLine("## Discovery");
            report.AppendLine($"- Scan roots: {string.Join(", ", discoveryRoots)}");
            report.AppendLine($"- Solutions: {slnFiles.Length}");
            report.AppendLine($"- Projects: {csprojFiles.Length}");
            report.AppendLine();

            var buildTarget = SelectBestBuildTarget(slnFiles, csprojFiles, outputPath);
            if (string.IsNullOrWhiteSpace(buildTarget))
            {
                context.AgentStatuses[Type] = AgentStatus.Failed;
                errors.Add($"No .sln or .csproj files found. Discovery roots: {string.Join(", ", discoveryRoots)}");
                report.AppendLine("- Build target: NOT FOUND (.sln/.csproj missing)");
                findings.Add(new ReviewFinding
                {
                    Category = "Build",
                    Severity = ReviewSeverity.Error,
                    Message = "Build target discovery failed. No .sln or .csproj found in discovery roots.",
                    FilePath = outputPath,
                    Suggestion = $"Check output path and ensure generated solution exists. Roots scanned: {string.Join(", ", discoveryRoots)}"
                });
                context.Findings.AddRange(findings);
                return Fail("No build target discovered", errors, sw.Elapsed);
            }
            report.AppendLine($"- Build target: {buildTarget}");
            var buildWorkDir = Path.GetDirectoryName(buildTarget) ?? outputPath;

            // ── Step 2: Restore ──
            report.AppendLine("## NuGet Restore");
            var (restoreOk, restoreOut) = await RunAsync("dotnet", $"restore \"{buildTarget}\"", buildWorkDir, ct);
            report.AppendLine(restoreOk ? "- **SUCCESS**" : $"- **FAILED**: {Truncate(restoreOut, 300)}");
            if (!restoreOk) errors.Add($"Restore failed: {Truncate(restoreOut, 200)}");
            report.AppendLine();

            // ── Step 3: Build ──
            report.AppendLine("## Compilation");
            var (buildOk, buildOut) = await RunAsync("dotnet",
                $"build \"{buildTarget}\" -c Release --no-restore -v normal", buildWorkDir, ct, 300_000);

            var buildErrors = ParseBuildErrors(buildOut);
            var buildWarnings = ParseBuildWarnings(buildOut);
            report.AppendLine($"- Result: **{(buildOk ? "SUCCESS" : "FAILED")}**");
            report.AppendLine($"- Errors: {buildErrors.Count}");
            report.AppendLine($"- Warnings: {buildWarnings.Count}");

            foreach (var err in buildErrors.Take(20))
            {
                report.AppendLine($"  - `{err.File}({err.Line})`: {err.Message}");
                findings.Add(new ReviewFinding
                {
                    FilePath = err.File,
                    LineNumber = err.Line,
                    Severity = ReviewSeverity.Error,
                    Category = "Build",
                    Message = err.Message,
                    Suggestion = "Fix compilation error to proceed with deployment."
                });
            }

            foreach (var warn in buildWarnings.Take(30))
            {
                findings.Add(new ReviewFinding
                {
                    FilePath = warn.File,
                    LineNumber = warn.Line,
                    Severity = ReviewSeverity.Warning,
                    Category = "Build",
                    Message = warn.Message,
                    Suggestion = "Resolve warning to improve code quality."
                });
            }
            report.AppendLine();

            // ── Step 4: AI Fix Suggestions for errors ──
            if (buildErrors.Count > 0 && buildErrors.Count <= 10)
            {
                report.AppendLine("## AI Fix Suggestions");
                var errorBlock = string.Join("\n", buildErrors.Select(e => $"{e.File}({e.Line}): error {e.Code}: {e.Message}"));
                var prompt = $"""
                    You are an {context.ExpertRoleLabel()}. Analyze these build errors and suggest precise fixes.
                    For each error, provide: 1) Root cause 2) Fix (code snippet if applicable).
                    Keep suggestions concise.

                    Build errors:
                    {errorBlock}
                    """;
                try
                {
                    var suggestion = await _llm.GenerateAsync(prompt, ct);
                    report.AppendLine(suggestion);
                }
                catch (Exception ex)
                {
                    report.AppendLine($"- AI suggestion unavailable: {ex.Message}");
                }
                report.AppendLine();
            }

            // ── Step 5: Test run ──
            report.AppendLine("## Test Execution");
            if (buildOk)
            {
                var testProjects = csprojFiles.Where(f => f.Contains("Test", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (testProjects.Length > 0)
                {
                    var (testOk, testOut) = await RunAsync("dotnet",
                        $"test \"{buildTarget}\" --no-build -c Release --verbosity normal", buildWorkDir, ct, 300_000);
                    var testSummary = ParseTestSummary(testOut);
                    report.AppendLine($"- Result: **{(testOk ? "PASSED" : "FAILED")}**");
                    report.AppendLine($"- {testSummary}");
                    if (!testOk) errors.Add($"Tests failed: {testSummary}");
                }
                else
                {
                    report.AppendLine("- No test projects found.");
                }
            }
            else
            {
                report.AppendLine("- Skipped (build failed).");
            }
            report.AppendLine();

            // ── Summary ──
            report.AppendLine("## Summary");
            report.AppendLine($"- Build: {(buildOk ? "PASS" : "FAIL")}");
            report.AppendLine($"- Errors: {buildErrors.Count} | Warnings: {buildWarnings.Count}");
            report.AppendLine($"- Findings: {findings.Count}");
            report.AppendLine($"- Duration: {sw.Elapsed.TotalSeconds:F1}s");

            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Infrastructure,
                RelativePath = "build/build-report.md",
                FileName = "build-report.md",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-BUILD-01"],
                Content = report.ToString()
            });

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = buildOk ? AgentStatus.Completed : AgentStatus.Failed;

            // Dispatch build findings as feedback to responsible code-gen agents
            if (findings.Count > 0)
                context.DispatchFindingsAsFeedback(Type, findings);

            // Notify code-gen agents about build failures so they can fix on next iteration
            if (!buildOk)
            {
                var errorSummary = string.Join("; ", buildErrors.Take(5).Select(e => $"{Path.GetFileName(e.File)}({e.Line}): {e.Message}"));
                context.WriteFeedback(AgentType.Database, Type, $"Build failed with {buildErrors.Count} errors. DB-related: {errorSummary}");
                context.WriteFeedback(AgentType.ServiceLayer, Type, $"Build failed with {buildErrors.Count} errors. Service-related: {errorSummary}");
                context.WriteFeedback(AgentType.Application, Type, $"Build failed with {buildErrors.Count} errors. App-related: {errorSummary}");
                context.WriteFeedback(AgentType.BugFix, Type, $"Build failed — {buildErrors.Count} compilation errors need fixing: {errorSummary}");
            }

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type,
                Success = buildOk,
                Summary = $"Build Agent: {(buildOk ? "PASS" : "FAIL")} — {buildErrors.Count} errors, {buildWarnings.Count} warnings, {findings.Count} findings",
                Artifacts = artifacts,
                Findings = findings,
                Errors = errors,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "BuildAgent failed");
            return Fail(ex.Message, [ex.Message], sw.Elapsed);
        }
    }

    private record BuildDiagnostic(string File, int Line, string Code, string Message);

    private static List<BuildDiagnostic> ParseBuildErrors(string output)
    {
        var results = new List<BuildDiagnostic>();
        foreach (Match m in Regex.Matches(output, @"(.+?)\((\d+),\d+\):\s+error\s+(CS\d+):\s+(.+)"))
            results.Add(new(m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value), m.Groups[3].Value, m.Groups[4].Value.Trim()));
        return results;
    }

    private static List<BuildDiagnostic> ParseBuildWarnings(string output)
    {
        var results = new List<BuildDiagnostic>();
        foreach (Match m in Regex.Matches(output, @"(.+?)\((\d+),\d+\):\s+warning\s+(CS\d+):\s+(.+)"))
            results.Add(new(m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value), m.Groups[3].Value, m.Groups[4].Value.Trim()));
        return results;
    }

    private static string ParseTestSummary(string output)
    {
        var match = Regex.Match(output, @"(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)");
        return match.Success
            ? $"Passed: {match.Groups[3].Value}, Failed: {match.Groups[2].Value}, Skipped: {match.Groups[4].Value}, Total: {match.Groups[5].Value}"
            : "Test summary not parseable.";
    }

    private async Task<(bool Ok, string Output)> RunAsync(string cmd, string args, string workDir, CancellationToken ct, int timeoutMs = 120_000)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var psi = new ProcessStartInfo
            {
                FileName = cmd, Arguments = args, WorkingDirectory = workDir,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "Failed to start process");
            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            return (proc.ExitCode == 0, $"{stdout}\n{stderr}".Trim());
        }
        catch (OperationCanceledException) { return (false, $"Timed out after {timeoutMs}ms"); }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static IEnumerable<string> BuildDiscoveryRoots(string outputPath, AgentContext context)
    {
        var roots = new List<string>();
        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) return;
            if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
        }

        AddRoot(outputPath);
        AddRoot(Directory.GetCurrentDirectory());
        AddRoot(context.RequirementsBasePath);

        var parent = Directory.GetParent(outputPath)?.FullName;
        for (var i = 0; i < 3 && !string.IsNullOrWhiteSpace(parent); i++)
        {
            AddRoot(parent);
            parent = Directory.GetParent(parent!)?.FullName;
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateBuildFiles(IEnumerable<string> roots, string pattern)
    {
        foreach (var root in roots)
        {
            foreach (var file in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return file;
            }
        }
    }

    private static string? SelectBestBuildTarget(IReadOnlyCollection<string> slnFiles, IReadOnlyCollection<string> csprojFiles, string outputPath)
    {
        if (slnFiles.Count > 0)
        {
            var preferredSln = slnFiles.FirstOrDefault(s =>
                Path.GetFileName(s).Equals("GNex.sln", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferredSln)) return preferredSln;

            return slnFiles
                .OrderBy(s => Path.GetRelativePath(outputPath, s).Count(c => c == Path.DirectorySeparatorChar))
                .First();
        }

        if (csprojFiles.Count > 0)
        {
            var preferredWeb = csprojFiles.FirstOrDefault(p =>
                Path.GetFileName(p).Equals("GNex.Studio.csproj", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferredWeb)) return preferredWeb;

            return csprojFiles
                .OrderBy(p => Path.GetRelativePath(outputPath, p).Count(c => c == Path.DirectorySeparatorChar))
                .First();
        }

        return null;
    }

    private static AgentResult Fail(string msg, List<string> errs, TimeSpan d) => new()
    { Agent = AgentType.Build, Success = false, Summary = $"Build Agent: {msg}", Errors = errs, Duration = d };

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
