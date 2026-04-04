using System.Diagnostics;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using HmsAgents.Web.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/pipeline")]
public sealed class PipelineController : ControllerBase
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly IConfiguration _config;

    public PipelineController(
        IAgentOrchestrator orchestrator,
        IHubContext<PipelineHub> hub,
        IConfiguration config)
    {
        _orchestrator = orchestrator;
        _hub = hub;
        _config = config;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunPipeline([FromBody] PipelineRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RequirementsPath) || string.IsNullOrWhiteSpace(request.OutputPath))
            return BadRequest(new { error = "requirementsPath and outputPath are required." });

        if (!Directory.Exists(request.RequirementsPath))
            return BadRequest(new { error = $"Requirements path not found: {request.RequirementsPath}" });

        // Ensure output directory exists
        Directory.CreateDirectory(request.OutputPath);

        var sw = Stopwatch.StartNew();

        var dbHost = request.DbHost ?? "localhost";
        var dbUser = request.DbUser ?? "hms_admin";
        var connStr = $"Host={dbHost};Port={request.DbPort};Database={request.DbName};Username={dbUser};Password={request.DbPassword}";

        var pipelineConfig = new PipelineConfig
        {
            RequirementsPath = request.RequirementsPath,
            OutputPath = request.OutputPath,
            SolutionNamespace = request.SolutionNamespace ?? "Hms",
            DbConnectionString = connStr,
            DockerContainerName = request.DockerContainerName ?? "hms-postgres",
            DbHost = dbHost,
            DbPort = request.DbPort > 0 ? request.DbPort : 5432,
            DbName = request.DbName ?? "hms_db",
            DbPassword = request.DbPassword ?? "hms_secure_pwd_2026!",
            DbUser = dbUser,
            SpinUpDocker = request.SpinUpDocker,
            ExecuteDdl = request.ExecuteDdl,
            ServicePorts = request.ServicePorts ?? new ServicePortMap()
        };

        var context = await _orchestrator.RunPipelineAsync(pipelineConfig, ct);

        // Push completion summary over SignalR
        await _hub.Clients.All.SendAsync("PipelineComplete", new
        {
            requirementCount = context.Requirements.Count,
            artifactCount = context.Artifacts.Count,
            findingCount = context.Findings.Count,
            testDiagnosticCount = context.TestDiagnostics.Count,
            durationMs = sw.Elapsed.TotalMilliseconds,
            findings = context.Findings.Select(f => new
            {
                f.Category,
                f.Message,
                f.FilePath,
                severity = (int)f.Severity,
                f.Suggestion
            }),
            artifacts = context.Artifacts.Select(a => new
            {
                a.RelativePath,
                a.FileName,
                layer = (int)a.Layer,
                producedBy = (int)a.ProducedBy,
                a.Namespace
            }),
            testDiagnostics = context.TestDiagnostics.Select(d => new
            {
                d.TestName,
                d.AgentUnderTest,
                outcome = (int)d.Outcome,
                d.Diagnostic,
                d.Remediation,
                d.Category,
                d.DurationMs,
                d.AttemptNumber
            })
        }, ct);

        return Ok(new
        {
            runId = context.RunId,
            requirements = context.Requirements.Count,
            artifacts = context.Artifacts.Count,
            findings = context.Findings.Count,
            testDiagnostics = context.TestDiagnostics.Count,
            durationMs = sw.Elapsed.TotalMilliseconds
        });
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null)
            return Ok(new { status = "no_run" });

        return Ok(new
        {
            runId = ctx.RunId,
            agentStatuses = ctx.AgentStatuses.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.ToString()),
            requirements = ctx.Requirements.Count,
            artifacts = ctx.Artifacts.Count,
            findings = ctx.Findings.Count,
            completed = ctx.CompletedAt.HasValue
        });
    }

    [HttpGet("artifacts")]
    public IActionResult GetArtifacts()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null) return Ok(Array.Empty<object>());

        return Ok(ctx.Artifacts.Select(a => new
        {
            a.Id,
            layer = a.Layer.ToString(),
            a.RelativePath,
            a.FileName,
            a.Namespace,
            producedBy = a.ProducedBy.ToString(),
            tracedRequirements = a.TracedRequirementIds.Count,
            contentLength = a.Content.Length
        }));
    }

    [HttpGet("findings")]
    public IActionResult GetFindings()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null) return Ok(Array.Empty<object>());

        return Ok(ctx.Findings.Select(f => new
        {
            f.Id,
            f.Category,
            severity = f.Severity.ToString(),
            f.Message,
            f.FilePath,
            f.Suggestion,
            f.TracedRequirementId
        }));
    }

    private static readonly string s_configPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "pipeline-config.json");

    [HttpGet("config")]
    public IActionResult GetSavedConfig()
    {
        var path = Path.GetFullPath(s_configPath);
        if (!System.IO.File.Exists(path))
            return Ok(new { });
        var json = System.IO.File.ReadAllText(path);
        return Content(json, "application/json");
    }

    [HttpPost("config")]
    public IActionResult SaveConfig([FromBody] System.Text.Json.JsonElement body)
    {
        var path = Path.GetFullPath(s_configPath);
        System.IO.File.WriteAllText(path, body.GetRawText());
        return Ok(new { saved = true, path });
    }
}

public sealed class PipelineRunRequest
{
    public string RequirementsPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string? SolutionNamespace { get; init; }
    public string? DockerContainerName { get; init; }
    public string? DbHost { get; init; }
    public int DbPort { get; init; } = 5432;
    public string? DbName { get; init; }
    public string? DbPassword { get; init; }
    public string? DbUser { get; init; }
    public bool SpinUpDocker { get; init; } = true;
    public bool ExecuteDdl { get; init; } = true;
    public ServicePortMap? ServicePorts { get; init; }
}
