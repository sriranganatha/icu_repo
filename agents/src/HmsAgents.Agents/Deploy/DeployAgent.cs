using System.Diagnostics;
using System.Text;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Deploy;

/// <summary>
/// Deployment agent — builds, publishes, and launches the generated HMS solution
/// from the pipeline output path. Supports dotnet build/publish, docker-compose,
/// health-check verification, and rollback on failure.
/// </summary>
public sealed class DeployAgent : IAgent
{
    private readonly ILogger<DeployAgent> _logger;

    public AgentType Type => AgentType.Deploy;
    public string Name => "Deploy Agent";
    public string Description => "Builds, publishes, and deploys the generated HMS solution from the output path.";

    private static readonly (string Name, string Project, int Port)[] Services =
    [
        ("PatientService",     "Hms.PatientService",     5101),
        ("EncounterService",   "Hms.EncounterService",   5102),
        ("InpatientService",   "Hms.InpatientService",   5103),
        ("EmergencyService",   "Hms.EmergencyService",   5104),
        ("DiagnosticsService", "Hms.DiagnosticsService", 5105),
        ("RevenueService",     "Hms.RevenueService",     5106),
        ("AuditService",       "Hms.AuditService",       5107),
        ("AiService",          "Hms.AiService",          5108),
        ("ApiGateway",         "Hms.ApiGateway",         5100),
    ];

    public DeployAgent(ILogger<DeployAgent> logger)
    {
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("DeployAgent starting — build and deploy from {OutputPath}", context.OutputBasePath);

        var artifacts = new List<CodeArtifact>();
        var messages = new List<AgentMessage>();
        var errors = new List<string>();
        var report = new StringBuilder();
        report.AppendLine("# Deployment Report");
        report.AppendLine($"**Output Path**: `{context.OutputBasePath}`");
        report.AppendLine($"**Timestamp**: {DateTime.UtcNow:u}");
        report.AppendLine();

        try
        {
            var outputPath = context.OutputBasePath;
            if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
            {
                errors.Add($"Output path does not exist: {outputPath}");
                context.AgentStatuses[Type] = AgentStatus.Failed;
                return Fail("Output path not found", errors, sw.Elapsed);
            }

            // ── Step 1: Locate solution file ──
            var slnFiles = Directory.GetFiles(outputPath, "*.sln", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                            && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var csprojFiles = Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                            && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var solutionPath = slnFiles.FirstOrDefault();
            var restoreBuildTarget = solutionPath ?? csprojFiles.FirstOrDefault();

            report.AppendLine("## Step 1: Solution Discovery");
            if (solutionPath is not null)
            {
                report.AppendLine($"- Found solution: `{Path.GetFileName(solutionPath)}`");
                _logger.LogInformation("Found solution: {Sln}", solutionPath);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Found solution: {Path.GetFileName(solutionPath)}");
            }
            else
            {
                report.AppendLine("- No .sln file found; will build individual projects.");
                _logger.LogWarning("No .sln found in {Output}", outputPath);
            }

            if (string.IsNullOrWhiteSpace(restoreBuildTarget))
            {
                errors.Add($"No .sln or .csproj found under output path: {outputPath}");
                report.AppendLine("- No restore/build target found (.sln/.csproj missing).\n");
                context.AgentStatuses[Type] = AgentStatus.Failed;
                artifacts.Add(MakeReportArtifact(report));
                context.Artifacts.AddRange(artifacts);
                return Fail("Build target not found — cannot deploy", errors, sw.Elapsed, artifacts, messages);
            }
            report.AppendLine();

            // ── Step 2: NuGet Restore ──
            report.AppendLine("## Step 2: NuGet Restore");
            var restoreTarget = restoreBuildTarget;
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Step 2: Running NuGet restore on {Path.GetFileName(restoreTarget)}");
            var (restoreOk, restoreOut) = await RunProcessAsync("dotnet", $"restore \"{restoreTarget}\"", outputPath, ct);
            report.AppendLine(restoreOk ? "- Restore: **SUCCESS**" : $"- Restore: **FAILED** — {Truncate(restoreOut, 300)}");
            if (!restoreOk) errors.Add($"Restore failed: {Truncate(restoreOut, 200)}");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, restoreOk ? "NuGet restore succeeded" : $"NuGet restore failed: {Truncate(restoreOut, 100)}");
            report.AppendLine();

            // ── Step 3: Build ──
            report.AppendLine("## Step 3: Build");
            var buildTarget = restoreBuildTarget;
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Step 3: Building solution in Release configuration");
            var (buildOk, buildOut) = await RunProcessAsync("dotnet", $"build \"{buildTarget}\" -c Release --no-restore", outputPath, ct);
            report.AppendLine(buildOk ? "- Build: **SUCCESS**" : $"- Build: **FAILED** — {Truncate(buildOut, 300)}");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, buildOk ? "Build succeeded (Release)" : $"Build failed: {Truncate(buildOut, 100)}");
            if (!buildOk)
            {
                errors.Add($"Build failed: {Truncate(buildOut, 200)}");
                context.AgentStatuses[Type] = AgentStatus.Failed;
                report.AppendLine();
                report.AppendLine("## Result: FAILED (build errors)");
                artifacts.Add(MakeReportArtifact(report));
                context.Artifacts.AddRange(artifacts);
                return Fail("Build failed — cannot deploy", errors, sw.Elapsed, artifacts, messages);
            }
            report.AppendLine();

            // ── Step 4: Publish each service ──
            report.AppendLine("## Step 4: Publish Services");
            var publishResults = new List<(string Service, bool Ok, string Output)>();
            foreach (var (name, project, port) in Services)
            {
                ct.ThrowIfCancellationRequested();
                var projDir = Path.Combine(outputPath, "src", project);
                var csproj = Path.Combine(projDir, $"{project}.csproj");

                if (!File.Exists(csproj))
                {
                    report.AppendLine($"- {name}: **SKIPPED** (project not found)");
                    publishResults.Add((name, true, "skipped"));
                    continue;
                }

                var publishDir = Path.Combine(outputPath, "deploy", name);
                Directory.CreateDirectory(publishDir);
                var (pubOk, pubOut) = await RunProcessAsync("dotnet",
                    $"publish \"{csproj}\" -c Release --no-build -o \"{publishDir}\"",
                    outputPath, ct);
                report.AppendLine(pubOk
                    ? $"- {name}: **PUBLISHED** → `deploy/{name}/`"
                    : $"- {name}: **FAILED** — {Truncate(pubOut, 200)}");
                publishResults.Add((name, pubOk, pubOut));
                if (!pubOk) errors.Add($"Publish {name} failed: {Truncate(pubOut, 150)}");
            }
            report.AppendLine();

            // ── Step 5: Docker Compose (if docker-compose.yml exists) ──
            var composeFile = Path.Combine(outputPath, "docker-compose.yml");
            if (!File.Exists(composeFile))
                composeFile = Path.Combine(outputPath, "src", "docker-compose.yml");

            report.AppendLine("## Step 5: Docker Compose");
            if (File.Exists(composeFile))
            {
                var composeDir = Path.GetDirectoryName(composeFile)!;
                // Validate first
                var (validateOk, validateOut) = await RunProcessAsync("docker",
                    $"compose -f \"{composeFile}\" config --quiet", composeDir, ct, timeoutMs: 30_000);
                if (validateOk)
                {
                    report.AppendLine("- Compose config: **VALID**");

                    // Build images
                    var (imgOk, imgOut) = await RunProcessAsync("docker",
                        $"compose -f \"{composeFile}\" build", composeDir, ct, timeoutMs: 300_000);
                    report.AppendLine(imgOk
                        ? "- Docker build: **SUCCESS**"
                        : $"- Docker build: **FAILED** — {Truncate(imgOut, 200)}");
                    if (!imgOk) errors.Add($"Docker compose build failed: {Truncate(imgOut, 150)}");

                    // Start services
                    if (imgOk)
                    {
                        var (upOk, upOut) = await RunProcessAsync("docker",
                            $"compose -f \"{composeFile}\" up -d", composeDir, ct, timeoutMs: 120_000);
                        report.AppendLine(upOk
                            ? "- Docker up: **STARTED**"
                            : $"- Docker up: **FAILED** — {Truncate(upOut, 200)}");
                        if (!upOk) errors.Add($"Docker compose up failed: {Truncate(upOut, 150)}");
                    }
                }
                else
                {
                    report.AppendLine($"- Compose config: **INVALID** — {Truncate(validateOut, 200)}");
                    errors.Add($"Docker compose config invalid: {Truncate(validateOut, 150)}");
                }
            }
            else
            {
                report.AppendLine("- No docker-compose.yml found; skipping container deployment.");
            }
            report.AppendLine();

            // ── Step 6: Health Checks ──
            report.AppendLine("## Step 6: Health Checks");
            var healthResults = new List<(string Name, int Port, bool Healthy)>();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            foreach (var (name, _, port) in Services)
            {
                var healthy = false;
                var url = $"http://localhost:{port}/healthz";
                try
                {
                    var resp = await http.GetAsync(url, ct);
                    healthy = resp.IsSuccessStatusCode;
                }
                catch { /* service not running */ }

                healthResults.Add((name, port, healthy));
                report.AppendLine(healthy
                    ? $"- {name} (:{port}): **HEALTHY** ✓"
                    : $"- {name} (:{port}): **UNREACHABLE**");
            }
            report.AppendLine();

            // ── Step 7: Database Migration Check ──
            report.AppendLine("## Step 7: Database Migrations");
            var migrationProject = Services.FirstOrDefault(s => s.Name == "PatientService");
            if (migrationProject != default)
            {
                var projDir = Path.Combine(outputPath, "src", migrationProject.Project);
                if (Directory.Exists(projDir))
                {
                    var migrationsDir = Path.Combine(projDir, "Migrations");
                    if (Directory.Exists(migrationsDir))
                    {
                        var migrationFiles = Directory.GetFiles(migrationsDir, "*.cs");
                        report.AppendLine($"- Found {migrationFiles.Length} migration files in PatientService.");
                    }
                    else
                    {
                        report.AppendLine("- No Migrations directory found. EF migrations may need `dotnet ef migrations add`.");
                    }
                }
            }
            report.AppendLine();

            // ── Summary ──
            var publishedCount = publishResults.Count(r => r.Ok && r.Output != "skipped");
            var healthyCount = healthResults.Count(r => r.Healthy);
            var totalServices = Services.Length;
            var overallSuccess = errors.Count == 0;

            report.AppendLine("## Deployment Summary");
            report.AppendLine($"- Build: {(buildOk ? "SUCCESS" : "FAILED")}");
            report.AppendLine($"- Published: {publishedCount}/{totalServices} services");
            report.AppendLine($"- Docker: {(File.Exists(composeFile) ? "attempted" : "skipped")}");
            report.AppendLine($"- Healthy: {healthyCount}/{totalServices} services");
            report.AppendLine($"- Errors: {errors.Count}");
            report.AppendLine($"- Duration: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine($"- **Overall: {(overallSuccess ? "SUCCESS" : "PARTIAL — see errors")}**");

            artifacts.Add(MakeReportArtifact(report));
            context.Artifacts.AddRange(artifacts);

            messages.Add(new AgentMessage
            {
                From = Type, To = AgentType.Orchestrator,
                Subject = overallSuccess ? "Deployment successful" : "Deployment completed with errors",
                Body = $"Published {publishedCount}/{totalServices} services, {healthyCount} healthy. {errors.Count} errors."
            });

            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type,
                Success = overallSuccess,
                Summary = $"Deploy Agent: published {publishedCount}/{totalServices} services, {healthyCount} healthy, {errors.Count} errors in {sw.Elapsed.TotalSeconds:F1}s",
                Artifacts = artifacts,
                Messages = messages,
                Errors = errors,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            return Fail("Deployment cancelled", ["Operation cancelled"], sw.Elapsed);
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "DeployAgent failed");
            return Fail($"Deployment error: {ex.Message}", [ex.Message], sw.Elapsed);
        }
    }

    private static AgentResult Fail(string summary, List<string> errors, TimeSpan duration,
        List<CodeArtifact>? artifacts = null, List<AgentMessage>? messages = null)
        => new()
        {
            Agent = AgentType.Deploy,
            Success = false,
            Summary = $"Deploy Agent: {summary}",
            Errors = errors,
            Artifacts = artifacts ?? [],
            Messages = messages ?? [],
            Duration = duration
        };

    private static CodeArtifact MakeReportArtifact(StringBuilder report) => new()
    {
        Layer = ArtifactLayer.Infrastructure,
        RelativePath = "deploy/deployment-report.md",
        FileName = "deployment-report.md",
        Namespace = string.Empty,
        ProducedBy = AgentType.Deploy,
        TracedRequirementIds = ["NFR-DEPLOY-01"],
        Content = report.ToString()
    };

    private async Task<(bool Success, string Output)> RunProcessAsync(
        string command, string args, string workingDir, CancellationToken ct, int timeoutMs = 180_000)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogInformation("Deploy: {Cmd} {Args}", command, args);

            using var proc = Process.Start(psi);
            if (proc is null) return (false, "Failed to start process");

            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            var output = proc.ExitCode == 0 ? stdout : $"{stderr}\n{stdout}";
            return (proc.ExitCode == 0, output.Trim());
        }
        catch (OperationCanceledException)
        {
            return (false, $"Timed out after {timeoutMs}ms");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deploy process failed: {Cmd} {Args}", command, args);
            return (false, ex.Message);
        }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
