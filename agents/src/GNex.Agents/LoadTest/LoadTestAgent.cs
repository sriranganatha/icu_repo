using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.LoadTest;

/// <summary>
/// Load test agent — generates k6 load test scripts, performance benchmarks,
/// and stress test scenarios for all HMS microservice endpoints. Produces
/// runnable scripts with realistic healthcare workload patterns.
/// </summary>
public sealed class LoadTestAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<LoadTestAgent> _logger;

    public AgentType Type => AgentType.LoadTest;
    public string Name => "Load Test Agent";
    public string Description => "Generates k6/JMeter load test scripts with realistic healthcare workload patterns.";

    private static readonly (string Service, int Port, string[] Endpoints)[] ServiceEndpoints =
    [
        ("PatientService", 5101, ["/api/patients", "/api/patients/{id}", "/api/patients/search"]),
        ("EncounterService", 5102, ["/api/encounters", "/api/encounters/{id}", "/api/encounters/{id}/notes"]),
        ("InpatientService", 5103, ["/api/admissions", "/api/admissions/{id}", "/api/beds"]),
        ("EmergencyService", 5104, ["/api/emergency/arrivals", "/api/emergency/triage"]),
        ("DiagnosticsService", 5105, ["/api/diagnostics/results", "/api/diagnostics/orders"]),
        ("RevenueService", 5106, ["/api/claims", "/api/billing"]),
        ("AuditService", 5107, ["/api/audit/logs", "/api/audit/events"]),
        ("AiService", 5108, ["/api/ai/interactions", "/api/ai/copilot"]),
        ("ApiGateway", 5100, ["/api/health", "/api/gateway/routes"]),
    ];

    public LoadTestAgent(ILlmProvider llm, ILogger<LoadTestAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("LoadTestAgent starting");

        var artifacts = new List<CodeArtifact>();

        try
        {
            // ── Per-service k6 scripts ──
            foreach (var (svc, port, endpoints) in ServiceEndpoints)
            {
                ct.ThrowIfCancellationRequested();
                var prompt = $"""
                    Generate a k6 load test script (JavaScript) for the HMS {svc} running on port {port}.
                    
                    Endpoints to test:
                    {string.Join("\n", endpoints.Select(e => $"  - GET http://localhost:{port}{e}"))}
                    
                    Requirements:
                    - Stages: ramp-up (1m, 50 VUs), steady (3m, 100 VUs), spike (30s, 200 VUs), cool-down (1m, 10 VUs)
                    - Add realistic healthcare request bodies for POST endpoints
                    - Include JWT auth token in headers
                    - Thresholds: p95 < 500ms, error rate < 1%, throughput > 100 rps
                    - Include check() assertions on response status and body
                    - Add custom metrics for each endpoint
                    - Include setup() for auth token and teardown() for cleanup
                    - Add sleep(1) between iterations
                    
                    Return ONLY the k6 JavaScript code, no explanations.
                    """;

                var script = await _llm.GenerateAsync(prompt, ct);
                script = script.Replace("```javascript", "").Replace("```js", "").Replace("```", "").Trim();

                artifacts.Add(new CodeArtifact
                {
                    Layer = ArtifactLayer.Test,
                    RelativePath = $"tests/load/{svc.ToLowerInvariant()}-load-test.js",
                    FileName = $"{svc.ToLowerInvariant()}-load-test.js",
                    Namespace = string.Empty,
                    ProducedBy = Type,
                    TracedRequirementIds = ["NFR-LOADTEST-01"],
                    Content = script
                });
            }

            // ── Stress test scenario ──
            var stressPrompt = $"""
                Generate a k6 stress test script that tests ALL HMS services simultaneously.
                Services: {string.Join(", ", ServiceEndpoints.Select(s => $"{s.Service}(:{s.Port})"))}
                
                Requirements:
                - Simulate hospital peak hours: 500 concurrent users
                - Realistic workflow: patient registration → encounter → diagnosis → billing
                - Stages: warm-up (2m, 50VU), normal (5m, 200VU), peak (3m, 500VU), recovery (2m, 50VU)
                - Breakpoint detection: find the max VUs before p95 > 2s
                - Export results as JSON summary
                - Thresholds: p99 < 2s, error rate < 5%
                
                Return ONLY k6 JavaScript.
                """;
            var stressScript = await _llm.GenerateAsync(stressPrompt, ct);
            stressScript = stressScript.Replace("```javascript", "").Replace("```js", "").Replace("```", "").Trim();
            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Test,
                RelativePath = "tests/load/stress-test-full-system.js",
                FileName = "stress-test-full-system.js",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-LOADTEST-02"],
                Content = stressScript
            });

            // ── Benchmark config ──
            artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Test,
                RelativePath = "tests/load/run-load-tests.ps1",
                FileName = "run-load-tests.ps1",
                Namespace = string.Empty,
                ProducedBy = Type,
                TracedRequirementIds = ["NFR-LOADTEST-03"],
                Content = """
                    # HMS Load Test Runner
                    param(
                        [string]$TestType = "all",
                        [string]$Service = "",
                        [string]$OutputDir = "./load-test-results"
                    )

                    if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
                        Write-Host "k6 not found. Install: https://k6.io/docs/get-started/installation/" -ForegroundColor Red
                        exit 1
                    }

                    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
                    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

                    if ($TestType -eq "stress" -or $TestType -eq "all") {
                        Write-Host "Running full-system stress test..." -ForegroundColor Cyan
                        k6 run --out json="$OutputDir/stress-$timestamp.json" tests/load/stress-test-full-system.js
                    }

                    if ($TestType -eq "service" -and $Service) {
                        $scriptName = "$($Service.ToLower())-load-test.js"
                        $scriptPath = "tests/load/$scriptName"
                        if (Test-Path $scriptPath) {
                            Write-Host "Running load test for $Service..." -ForegroundColor Cyan
                            k6 run --out json="$OutputDir/$Service-$timestamp.json" $scriptPath
                        } else {
                            Write-Host "Script not found: $scriptPath" -ForegroundColor Red
                        }
                    }

                    if ($TestType -eq "all") {
                        $scripts = Get-ChildItem tests/load/*-load-test.js
                        foreach ($s in $scripts) {
                            Write-Host "Running $($s.Name)..." -ForegroundColor Cyan
                            k6 run --out json="$OutputDir/$($s.BaseName)-$timestamp.json" $s.FullName
                        }
                    }

                    Write-Host "`nResults saved to $OutputDir" -ForegroundColor Green
                    """
            });

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Load Test Agent: {artifacts.Count} artifacts — {ServiceEndpoints.Length} service scripts + stress test + runner",
                Artifacts = artifacts, Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "LoadTestAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }
}
