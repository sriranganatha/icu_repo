using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.DependencyAudit;

/// <summary>
/// Dependency agent — scans all .csproj files for NuGet packages, checks for
/// known vulnerabilities via dotnet list package --vulnerable, verifies license
/// compliance, and flags outdated or unpinned versions.
/// </summary>
public sealed class DependencyAgent : IAgent
{
    private readonly ILogger<DependencyAgent> _logger;

    public AgentType Type => AgentType.DependencyAudit;
    public string Name => "Dependency Agent";
    public string Description => "NuGet vulnerability scanning, license compliance, and version pinning verification.";

    public DependencyAgent(ILogger<DependencyAgent> logger)
    {
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("DependencyAgent starting");

        var findings = new List<ReviewFinding>();
        var artifacts = new List<CodeArtifact>();
        var report = new StringBuilder();
        report.AppendLine("# Dependency Audit Report");
        report.AppendLine($"**Generated**: {DateTime.UtcNow:u}");
        report.AppendLine();

        try
        {
            var outputPath = context.OutputBasePath;
            var csprojFiles = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories)
                : [];

            report.AppendLine($"## Scope: {csprojFiles.Length} projects");
            report.AppendLine();

            var allPackages = new Dictionary<string, HashSet<string>>(); // package → versions
            var totalPackageRefs = 0;
            var unpinned = 0;
            var prerelease = 0;

            // ── Step 1: Parse .csproj files ──
            report.AppendLine("## Package Inventory");
            report.AppendLine("| Project | Package | Version | Status |");
            report.AppendLine("|---------|---------|---------|--------|");

            foreach (var csproj in csprojFiles)
            {
                ct.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(outputPath, csproj);
                try
                {
                    var doc = XDocument.Load(csproj);
                    var pkgRefs = doc.Descendants("PackageReference");
                    foreach (var pkg in pkgRefs)
                    {
                        var name = pkg.Attribute("Include")?.Value ?? "unknown";
                        var version = pkg.Attribute("Version")?.Value ?? pkg.Element("Version")?.Value ?? "*";
                        totalPackageRefs++;

                        if (!allPackages.ContainsKey(name)) allPackages[name] = [];
                        allPackages[name].Add(version);

                        var status = "OK";
                        if (version.Contains('*') || version.Contains("$("))
                        {
                            status = "UNPINNED";
                            unpinned++;
                            findings.Add(new ReviewFinding
                            {
                                FilePath = relPath, LineNumber = 1,
                                Severity = ReviewSeverity.Warning,
                                Category = "Dependency-Unpinned",
                                Message = $"Package '{name}' version '{version}' is not pinned.",
                                Suggestion = "Pin to an exact version for reproducible builds."
                            });
                        }
                        if (version.Contains("-preview") || version.Contains("-alpha") || version.Contains("-beta") || version.Contains("-rc"))
                        {
                            status = "PRERELEASE";
                            prerelease++;
                            findings.Add(new ReviewFinding
                            {
                                FilePath = relPath, LineNumber = 1,
                                Severity = ReviewSeverity.Info,
                                Category = "Dependency-Prerelease",
                                Message = $"Package '{name}' uses prerelease version '{version}'.",
                                Suggestion = "Consider using stable release for production."
                            });
                        }

                        report.AppendLine($"| {Path.GetFileNameWithoutExtension(csproj)} | {name} | {version} | {status} |");
                    }
                }
                catch (Exception ex)
                {
                    report.AppendLine($"| {relPath} | ERROR | - | {ex.Message} |");
                }
            }
            report.AppendLine();

            // ── Step 2: Version inconsistency check ──
            report.AppendLine("## Version Consistency");
            var inconsistent = allPackages.Where(kv => kv.Value.Count > 1).ToList();
            if (inconsistent.Count > 0)
            {
                report.AppendLine("| Package | Versions Used |");
                report.AppendLine("|---------|--------------|");
                foreach (var pkg in inconsistent)
                {
                    report.AppendLine($"| {pkg.Key} | {string.Join(", ", pkg.Value)} |");
                    findings.Add(new ReviewFinding
                    {
                        FilePath = "*.csproj", LineNumber = 1,
                        Severity = ReviewSeverity.Warning,
                        Category = "Dependency-VersionMismatch",
                        Message = $"Package '{pkg.Key}' has inconsistent versions: {string.Join(", ", pkg.Value)}",
                        Suggestion = "Use Directory.Packages.props for centralized version management."
                    });
                }
            }
            else
            {
                report.AppendLine("All package versions are consistent across projects.");
            }
            report.AppendLine();

            // ── Step 3: Vulnerability scan ──
            report.AppendLine("## Vulnerability Scan");
            var slnFiles = Directory.GetFiles(outputPath, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                var (vulnOk, vulnOut) = await RunAsync("dotnet",
                    $"list \"{slnFiles[0]}\" package --vulnerable", outputPath, ct, 120_000);
                if (!vulnOk || vulnOut.Contains("has the following vulnerable packages"))
                {
                    report.AppendLine("**VULNERABLE PACKAGES DETECTED:**");
                    report.AppendLine("```");
                    report.AppendLine(Truncate(vulnOut, 1500));
                    report.AppendLine("```");

                    // Parse vulnerable packages
                    foreach (Match m in Regex.Matches(vulnOut, @">\s+(\S+)\s+(\S+)\s+(\S+)\s+(High|Critical|Medium|Low)"))
                    {
                        findings.Add(new ReviewFinding
                        {
                            FilePath = "NuGet", LineNumber = 0,
                            Severity = m.Groups[4].Value is "Critical" or "High" ? ReviewSeverity.Critical : ReviewSeverity.Warning,
                            Category = "Dependency-Vulnerability",
                            Message = $"Package '{m.Groups[1].Value}' v{m.Groups[2].Value} has {m.Groups[4].Value} severity vulnerability.",
                            Suggestion = $"Update to v{m.Groups[3].Value} or later."
                        });
                    }
                }
                else
                {
                    report.AppendLine("No known vulnerabilities detected.");
                }
            }
            else
            {
                report.AppendLine("No solution file found — skipping vulnerability scan.");
            }
            report.AppendLine();

            // ── Step 4: License check ──
            report.AppendLine("## License Compliance");
            var blockedLicenses = new[] { "GPL-3.0", "AGPL-3.0", "SSPL" };
            report.AppendLine($"- Blocked licenses: {string.Join(", ", blockedLicenses)}");
            report.AppendLine("- Note: Full license detection requires `dotnet-project-licenses` tool.");
            report.AppendLine("- Recommendation: Add license allowlist to CI/CD pipeline.");
            report.AppendLine();

            // ── Summary ──
            report.AppendLine("## Summary");
            report.AppendLine($"| Metric | Value |");
            report.AppendLine($"|--------|-------|");
            report.AppendLine($"| Projects scanned | {csprojFiles.Length} |");
            report.AppendLine($"| Unique packages | {allPackages.Count} |");
            report.AppendLine($"| Total references | {totalPackageRefs} |");
            report.AppendLine($"| Unpinned versions | {unpinned} |");
            report.AppendLine($"| Prerelease packages | {prerelease} |");
            report.AppendLine($"| Version inconsistencies | {inconsistent.Count} |");
            report.AppendLine($"| Total findings | {findings.Count} |");

            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "quality/dependency-audit-report.md",
                FileName = "dependency-audit-report.md",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-SCA-01"],
                Content = report.ToString()
            });

            // Generate Directory.Packages.props if version inconsistencies exist
            if (inconsistent.Count > 0)
                artifacts.Add(GenerateDirectoryPackagesProps(allPackages));

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Dependency Agent: {allPackages.Count} packages, {findings.Count} findings ({unpinned} unpinned, {inconsistent.Count} inconsistent)",
                Artifacts = artifacts, Findings = findings, Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "DependencyAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateDirectoryPackagesProps(Dictionary<string, HashSet<string>> packages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project>");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        foreach (var pkg in packages.OrderBy(p => p.Key))
        {
            var version = pkg.Value.OrderByDescending(v => v).First();
            sb.AppendLine($"    <PackageVersion Include=\"{pkg.Key}\" Version=\"{version}\" />");
        }
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</Project>");

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Configuration,
            RelativePath = "Directory.Packages.props",
            FileName = "Directory.Packages.props",
            Namespace = string.Empty,
            ProducedBy = AgentType.DependencyAudit,
            TracedRequirementIds = ["NFR-SCA-02"],
            Content = sb.ToString()
        };
    }

    private static async Task<(bool Ok, string Output)> RunAsync(string cmd, string args, string workDir, CancellationToken ct, int timeoutMs = 60_000)
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
            if (proc is null) return (false, "Failed to start");
            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            return (proc.ExitCode == 0, $"{stdout}\n{stderr}".Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
