using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Monitor;

/// <summary>
/// Monitor agent — watches deployed Docker containers, checks service health,
/// captures logs, and reports issues back via findings so the orchestrator can
/// dispatch BugFix → Build → Deploy remediation cycles.
/// </summary>
public sealed class MonitorAgent : IAgent
{
    private readonly ILogger<MonitorAgent> _logger;

    public AgentType Type => AgentType.Monitor;
    public string Name => "Monitor Agent";
    public string Description => "Monitors deployed Docker containers and services — health checks, log inspection, issue detection, and automated feedback for remediation.";

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

    public MonitorAgent(ILogger<MonitorAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("MonitorAgent starting — checking Docker containers and service health");

        var artifacts = new List<CodeArtifact>();
        var findings = new List<ReviewFinding>();
        var errors = new List<string>();
        var report = new StringBuilder();
        report.AppendLine("# Monitor Report");
        report.AppendLine($"**Timestamp**: {DateTime.UtcNow:u}");
        report.AppendLine();

        try
        {
            // ── Step 1: Check Docker containers ──
            report.AppendLine("## Docker Container Status");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Step 1: Checking Docker container status...");

            var containerResults = await CheckDockerContainersAsync(context, ct);
            foreach (var (name, status, healthy, logs) in containerResults)
            {
                var icon = healthy ? "✓" : "✗";
                report.AppendLine($"- {name}: **{status}** {icon}");

                if (!healthy)
                {
                    var errorMsg = $"Container '{name}' is {status}";
                    errors.Add(errorMsg);
                    findings.Add(new ReviewFinding
                    {
                        Category = "Deployment",
                        Severity = ReviewSeverity.Error,
                        Message = errorMsg,
                        Suggestion = "Fix container issue and redeploy — check Docker logs for root cause.",
                        FilePath = "docker-compose.yml"
                    });

                    if (!string.IsNullOrWhiteSpace(logs))
                    {
                        report.AppendLine($"  - Last logs: `{Truncate(logs, 200)}`");
                    }
                }
            }
            report.AppendLine();

            // ── Step 2: Service health checks ──
            report.AppendLine("## Service Health Checks");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Step 2: Running service health checks...");

            var healthResults = await CheckServiceHealthAsync(ct);
            var healthyCount = 0;
            foreach (var (name, port, healthy, statusCode, responseTime) in healthResults)
            {
                if (healthy)
                {
                    healthyCount++;
                    report.AppendLine($"- {name} (:{port}): **HEALTHY** ({responseTime}ms)");
                }
                else
                {
                    report.AppendLine($"- {name} (:{port}): **UNHEALTHY** (HTTP {statusCode})");
                    var errorMsg = $"Service '{name}' on port {port} is unhealthy (HTTP {statusCode})";
                    errors.Add(errorMsg);
                    findings.Add(new ReviewFinding
                    {
                        Category = "Deployment",
                        Severity = ReviewSeverity.Error,
                        Message = errorMsg,
                        Suggestion = $"Service {name} failed health check. Check application startup, port binding, and dependencies.",
                        FilePath = $"src/{Services.FirstOrDefault(s => s.Name == name).Project}/Program.cs"
                    });
                }
            }
            report.AppendLine();

            // ── Step 3: Docker logs analysis for crash patterns ──
            report.AppendLine("## Log Analysis");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Step 3: Analyzing Docker logs for error patterns...");

            var logIssues = await AnalyzeDockerLogsAsync(context, ct);
            if (logIssues.Count == 0)
            {
                report.AppendLine("- No critical error patterns detected in logs.");
            }
            else
            {
                foreach (var (container, issue, severity) in logIssues)
                {
                    report.AppendLine($"- **{container}**: {issue}");
                    findings.Add(new ReviewFinding
                    {
                        Category = "Runtime",
                        Severity = severity,
                        Message = $"[{container}] {issue}",
                        Suggestion = "Investigate runtime error and fix the root cause in source code."
                    });
                }
            }
            report.AppendLine();

            // ── Step 4: Resource usage check ──
            report.AppendLine("## Resource Usage");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Step 4: Checking container resource usage...");

            var resourceInfo = await CheckResourceUsageAsync(context, ct);
            if (!string.IsNullOrWhiteSpace(resourceInfo))
                report.AppendLine(resourceInfo);
            else
                report.AppendLine("- Resource stats unavailable.");
            report.AppendLine();

            // ── Step 5: Database connectivity check ──
            report.AppendLine("## Database Connectivity");
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Step 5: Checking database connectivity...");

            var dbConfig = context.PipelineConfig;
            if (dbConfig is not null)
            {
                var dbHealthy = await CheckDatabaseConnectivityAsync(dbConfig, ct);
                report.AppendLine(dbHealthy
                    ? $"- PostgreSQL at {dbConfig.DbHost}:{dbConfig.DbPort}/{dbConfig.DbName}: **CONNECTED**"
                    : $"- PostgreSQL at {dbConfig.DbHost}:{dbConfig.DbPort}/{dbConfig.DbName}: **UNREACHABLE**");

                if (!dbHealthy)
                {
                    errors.Add("Database is unreachable");
                    findings.Add(new ReviewFinding
                    {
                        Category = "Deployment",
                        Severity = ReviewSeverity.Critical,
                        Message = $"PostgreSQL database at {dbConfig.DbHost}:{dbConfig.DbPort} is unreachable",
                        Suggestion = "Ensure the PostgreSQL Docker container is running and accepting connections."
                    });
                }
            }
            report.AppendLine();

            // ── Summary ──
            report.AppendLine("## Summary");
            report.AppendLine($"- Docker containers checked: {containerResults.Count}");
            report.AppendLine($"- Services healthy: {healthyCount}/{Services.Length}");
            report.AppendLine($"- Log issues detected: {logIssues.Count}");
            report.AppendLine($"- Total findings: {findings.Count}");
            report.AppendLine($"- Duration: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine($"- **Overall: {(findings.Count == 0 ? "ALL CLEAR" : $"{findings.Count} ISSUES DETECTED")}**");

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type,
                    $"Monitor complete — {healthyCount}/{Services.Length} healthy, {findings.Count} issues, {logIssues.Count} log warnings");

            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Infrastructure,
                RelativePath = "monitor/monitor-report.md",
                FileName = "monitor-report.md",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-MONITOR-01"],
                Content = report.ToString()
            });

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type,
                Success = findings.Count == 0,
                Summary = $"Monitor Agent: {healthyCount}/{Services.Length} healthy, {findings.Count} issues detected, {logIssues.Count} log warnings in {sw.Elapsed.TotalSeconds:F1}s",
                Artifacts = artifacts,
                Findings = findings,
                Errors = errors,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "MonitorAgent failed");
            return new AgentResult
            {
                Agent = Type, Success = false,
                Summary = $"Monitor Agent failed: {ex.Message}",
                Errors = [ex.Message], Duration = sw.Elapsed
            };
        }
    }

    // ─── Docker container checks ───────────────────────────────────

    private async Task<List<(string Name, string Status, bool Healthy, string Logs)>> CheckDockerContainersAsync(
        AgentContext context, CancellationToken ct)
    {
        var results = new List<(string, string, bool, string)>();

        // Check the database container
        var dbContainer = context.PipelineConfig?.DockerContainerName ?? "ICU-postgres";
        var (exitCode, output) = await RunAsync("docker",
            $"inspect --format \"{{{{.State.Status}}}}\" {dbContainer}", ct);
        if (exitCode == 0)
        {
            var status = output.Trim();
            var healthy = status.Equals("running", StringComparison.OrdinalIgnoreCase);
            var logs = "";
            if (!healthy)
            {
                var (_, logOut) = await RunAsync("docker", $"logs --tail 20 {dbContainer}", ct);
                logs = logOut;
            }
            results.Add((dbContainer, status, healthy, logs));
        }
        else
        {
            results.Add((dbContainer, "not found", false, ""));
        }

        // Check service containers (from docker-compose)
        var (psExit, psOut) = await RunAsync("docker", "ps --format \"{{.Names}}|{{.Status}}|{{.State}}\"", ct);
        if (psExit == 0)
        {
            foreach (var line in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('|');
                if (parts.Length >= 3)
                {
                    var name = parts[0];
                    var status = parts[1];
                    var state = parts[2];
                    var healthy = state.Equals("running", StringComparison.OrdinalIgnoreCase);

                    // Only include HMS-related containers
                    if (name.Contains("hms", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("icu", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("patient", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("gateway", StringComparison.OrdinalIgnoreCase))
                    {
                        var logs = "";
                        if (!healthy)
                        {
                            var (_, logOut) = await RunAsync("docker", $"logs --tail 20 {name}", ct);
                            logs = logOut;
                        }
                        results.Add((name, status, healthy, logs));
                    }
                }
            }
        }

        return results;
    }

    // ─── Service health checks ─────────────────────────────────────

    private async Task<List<(string Name, int Port, bool Healthy, int StatusCode, long ResponseTimeMs)>> CheckServiceHealthAsync(
        CancellationToken ct)
    {
        var results = new List<(string, int, bool, int, long)>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        foreach (var (name, _, port) in Services)
        {
            var healthSw = Stopwatch.StartNew();
            try
            {
                var resp = await http.GetAsync($"http://localhost:{port}/healthz", ct);
                results.Add((name, port, resp.IsSuccessStatusCode, (int)resp.StatusCode, healthSw.ElapsedMilliseconds));
            }
            catch
            {
                results.Add((name, port, false, 0, healthSw.ElapsedMilliseconds));
            }
        }

        return results;
    }

    // ─── Docker log analysis ───────────────────────────────────────

    private async Task<List<(string Container, string Issue, ReviewSeverity Severity)>> AnalyzeDockerLogsAsync(
        AgentContext context, CancellationToken ct)
    {
        var issues = new List<(string, string, ReviewSeverity)>();
        var dbContainer = context.PipelineConfig?.DockerContainerName ?? "ICU-postgres";

        // Get recent logs from DB container
        var (exit, logs) = await RunAsync("docker", $"logs --tail 100 --since 1h {dbContainer}", ct);
        if (exit == 0 && !string.IsNullOrWhiteSpace(logs))
        {
            if (Regex.IsMatch(logs, @"FATAL|PANIC", RegexOptions.IgnoreCase))
                issues.Add((dbContainer, "FATAL/PANIC errors detected in PostgreSQL logs", ReviewSeverity.Critical));
            if (Regex.IsMatch(logs, @"out of memory|OOM", RegexOptions.IgnoreCase))
                issues.Add((dbContainer, "Out-of-memory condition detected", ReviewSeverity.Critical));
            if (Regex.IsMatch(logs, @"too many connections|connection limit", RegexOptions.IgnoreCase))
                issues.Add((dbContainer, "Connection limit reached", ReviewSeverity.Error));
            if (Regex.IsMatch(logs, @"permission denied|authentication failed", RegexOptions.IgnoreCase))
                issues.Add((dbContainer, "Authentication/permission failure detected", ReviewSeverity.Error));
            if (Regex.IsMatch(logs, @"deadlock detected", RegexOptions.IgnoreCase))
                issues.Add((dbContainer, "Deadlock detected in database", ReviewSeverity.Warning));
        }

        // Check service containers for common error patterns
        var (psExit, psOut) = await RunAsync("docker", "ps --format \"{{.Names}}\"", ct);
        if (psExit == 0)
        {
            foreach (var containerName in psOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = containerName.Trim();
                if (!name.Contains("hms", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("patient", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("gateway", StringComparison.OrdinalIgnoreCase))
                    continue;

                var (logExit, svcLogs) = await RunAsync("docker", $"logs --tail 50 --since 1h {name}", ct);
                if (logExit != 0 || string.IsNullOrWhiteSpace(svcLogs)) continue;

                if (Regex.IsMatch(svcLogs, @"Unhandled exception|System\.Exception|NullReferenceException", RegexOptions.IgnoreCase))
                    issues.Add((name, "Unhandled exception detected in service", ReviewSeverity.Error));
                if (Regex.IsMatch(svcLogs, @"fail:|error:|crit:", RegexOptions.IgnoreCase))
                    issues.Add((name, "Error-level log entries detected", ReviewSeverity.Warning));
                if (Regex.IsMatch(svcLogs, @"port.*already in use|address already in use", RegexOptions.IgnoreCase))
                    issues.Add((name, "Port binding conflict detected", ReviewSeverity.Error));
            }
        }

        return issues;
    }

    // ─── Resource usage ────────────────────────────────────────────

    private async Task<string> CheckResourceUsageAsync(AgentContext context, CancellationToken ct)
    {
        var (exit, output) = await RunAsync("docker",
            "stats --no-stream --format \"table {{.Name}}\\t{{.CPUPerc}}\\t{{.MemUsage}}\\t{{.NetIO}}\"", ct);
        if (exit != 0 || string.IsNullOrWhiteSpace(output)) return "";

        var sb = new StringBuilder();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("hms", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("icu", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("NAME", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("patient", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("gateway", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {line.Trim()}");
            }
        }
        return sb.ToString();
    }

    // ─── Database connectivity ─────────────────────────────────────

    private async Task<bool> CheckDatabaseConnectivityAsync(PipelineConfig config, CancellationToken ct)
    {
        var (exit, _) = await RunAsync("docker",
            $"exec {config.DockerContainerName} pg_isready -U {config.DbUser} -d {config.DbName}", ct, 10_000);
        return exit == 0;
    }

    // ─── Process runner ────────────────────────────────────────────

    private async Task<(int ExitCode, string Output)> RunAsync(
        string cmd, string args, CancellationToken ct, int timeoutMs = 30_000)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var psi = new ProcessStartInfo
            {
                FileName = cmd, Arguments = args,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (1, "Failed to start process");
            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            return (proc.ExitCode, $"{stdout}\n{stderr}".Trim());
        }
        catch (OperationCanceledException) { return (1, $"Timed out after {timeoutMs}ms"); }
        catch (Exception ex) { return (1, ex.Message); }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
