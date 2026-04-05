using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Build;

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
            var outputPath = context.OutputBasePath;
            if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
            {
                context.AgentStatuses[Type] = AgentStatus.Failed;
                return Fail("Output path not found", [$"Path does not exist: {outputPath}"], sw.Elapsed);
            }

            // ── Step 1: Find solution/projects ──
            var slnFiles = Directory.GetFiles(outputPath, "*.sln", SearchOption.TopDirectoryOnly);
            var csprojFiles = Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories);
            report.AppendLine("## Discovery");
            report.AppendLine($"- Solutions: {slnFiles.Length}");
            report.AppendLine($"- Projects: {csprojFiles.Length}");
            report.AppendLine();

            var buildTarget = slnFiles.Length > 0 ? slnFiles[0] : outputPath;

            // ── Step 2: Restore ──
            report.AppendLine("## NuGet Restore");
            var (restoreOk, restoreOut) = await RunAsync("dotnet", $"restore \"{buildTarget}\"", outputPath, ct);
            report.AppendLine(restoreOk ? "- **SUCCESS**" : $"- **FAILED**: {Truncate(restoreOut, 300)}");
            if (!restoreOk) errors.Add($"Restore failed: {Truncate(restoreOut, 200)}");
            report.AppendLine();

            // ── Step 3: Build ──
            report.AppendLine("## Compilation");
            var (buildOk, buildOut) = await RunAsync("dotnet",
                $"build \"{buildTarget}\" -c Release --no-restore -v normal", outputPath, ct, 300_000);

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
                    You are an expert .NET 8 developer. Analyze these build errors and suggest precise fixes.
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
                        $"test \"{buildTarget}\" --no-build -c Release --verbosity normal", outputPath, ct, 300_000);
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

    private static AgentResult Fail(string msg, List<string> errs, TimeSpan d) => new()
    { Agent = AgentType.Build, Success = false, Summary = $"Build Agent: {msg}", Errors = errs, Duration = d };

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
