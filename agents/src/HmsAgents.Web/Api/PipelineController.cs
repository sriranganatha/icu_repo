using System.Diagnostics;
using System.Text.Json;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using HmsAgents.Web.Hubs;
using HmsAgents.Web.Services;
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
    private readonly ILogger<PipelineController> _logger;
    private readonly PipelineStateStore _stateStore;
    private readonly AgentPipelineDb _db;
    private readonly IAuditLogger _audit;
    private readonly IHumanGate _humanGate;

    public PipelineController(
        IAgentOrchestrator orchestrator,
        IHubContext<PipelineHub> hub,
        IConfiguration config,
        ILogger<PipelineController> logger,
        PipelineStateStore stateStore,
        AgentPipelineDb db,
        IAuditLogger audit,
        IHumanGate humanGate)
    {
        _orchestrator = orchestrator;
        _hub = hub;
        _config = config;
        _logger = logger;
        _stateStore = stateStore;
        _db = db;
        _audit = audit;
        _humanGate = humanGate;
    }

    [HttpPost("run")]
    public IActionResult RunPipeline(
        [FromBody] PipelineRunRequest request,
        [FromServices] IHostApplicationLifetime lifetime)
    {
        if (string.IsNullOrWhiteSpace(request.RequirementsPath) || string.IsNullOrWhiteSpace(request.OutputPath))
            return BadRequest(new { error = "requirementsPath and outputPath are required." });

        if (!Directory.Exists(request.RequirementsPath))
            return BadRequest(new { error = $"Requirements path not found: {request.RequirementsPath}" });

        // Ensure output directory exists
        Directory.CreateDirectory(request.OutputPath);

        var dbHost = request.DbHost ?? "localhost";
        var dbUser = request.DbUser ?? "icu_admin";
        var connStr = $"Host={dbHost};Port={request.DbPort};Database={request.DbName};Username={dbUser};Password={request.DbPassword}";

        var pipelineConfig = new PipelineConfig
        {
            RequirementsPath = request.RequirementsPath,
            OutputPath = request.OutputPath,
            SolutionNamespace = request.SolutionNamespace ?? "Hms",
            DbConnectionString = connStr,
            DockerContainerName = request.DockerContainerName ?? "ICU-postgres",
            DbHost = dbHost,
            DbPort = request.DbPort > 0 ? request.DbPort : 5418,
            DbName = request.DbName ?? "icu_db",
            DbPassword = request.DbPassword ?? "ICU@1234",
            DbUser = dbUser,
            SpinUpDocker = request.SpinUpDocker,
            ExecuteDdl = request.ExecuteDdl,
            OrchestratorInstructions = request.OrchestratorInstructions ?? string.Empty,
            ServicePorts = request.ServicePorts ?? new ServicePortMap()
        };

        // Fire-and-forget: use app lifetime token so client disconnects don't cancel
        var appCt = lifetime.ApplicationStopping;
        var stateStore = _stateStore;
        var db = _db;
        var configJson = JsonSerializer.Serialize(pipelineConfig);

        // Persist initial orchestrator instructions to DB
        if (!string.IsNullOrWhiteSpace(pipelineConfig.OrchestratorInstructions))
            _db.SaveInstruction(null, pipelineConfig.OrchestratorInstructions, "PipelineStart");

        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            stateStore.Reset(Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var context = await _orchestrator.RunPipelineAsync(pipelineConfig, appCt);

                // ── Persist to JSON snapshot ──
                stateStore.TrackCompletion(
                    context.RunId,
                    context.Requirements.Count,
                    context.Artifacts.Count,
                    context.Findings.Count,
                    context.TestDiagnostics.Count,
                    context.ExpandedRequirements.ToList(),
                    sw.Elapsed.TotalMilliseconds);

                // ── Persist final state to SQLite DB ──
                db.CompleteRun(context.RunId,
                    context.Requirements.Count, context.Artifacts.Count,
                    context.Findings.Count, context.TestDiagnostics.Count,
                    context.ExpandedRequirements.Count, sw.Elapsed.TotalMilliseconds);

                db.SaveRequirements(context.RunId, context.Requirements.Select(r => new RequirementRow
                {
                    Id = r.Id, SourceFile = r.SourceFile, Section = r.Section,
                    HeadingLevel = r.HeadingLevel, Title = r.Title, Description = r.Description,
                    Module = r.Module, Tags = r.Tags.ToList(), AcceptanceCriteria = r.AcceptanceCriteria.ToList(),
                    DependsOn = r.DependsOn.ToList(), CreatedAt = DateTimeOffset.UtcNow
                }));

                db.SaveBacklogItems(context.RunId, context.ExpandedRequirements.Select(e => new BacklogItemRow
                {
                    Id = e.Id, ParentId = e.ParentId, SourceRequirementId = e.SourceRequirementId,
                    ItemType = e.ItemType.ToString(), Status = e.Status.ToString(),
                    Title = e.Title, Description = e.Description, Module = e.Module,
                    Priority = e.Priority, Iteration = e.Iteration,
                    AcceptanceCriteria = e.AcceptanceCriteria, DependsOn = e.DependsOn, Tags = e.Tags,
                    CreatedAt = e.CreatedAt, StartedAt = e.StartedAt, CompletedAt = e.CompletedAt,
                    AssignedAgent = e.AssignedAgent
                }));

                db.SaveFindings(context.RunId, context.Findings.Select(f => new FindingRow
                {
                    Id = f.Id, ArtifactId = f.ArtifactId, FilePath = f.FilePath,
                    LineNumber = f.LineNumber, Severity = f.Severity.ToString(),
                    Category = f.Category, Message = f.Message,
                    Suggestion = f.Suggestion, TracedRequirementId = f.TracedRequirementId
                }));

                db.SaveArtifacts(context.RunId, context.Artifacts.Select(a => new ArtifactRow
                {
                    Id = a.Id, Layer = a.Layer.ToString(), RelativePath = a.RelativePath,
                    FileName = a.FileName, Namespace = a.Namespace,
                    ProducedBy = a.ProducedBy.ToString(), ContentLength = a.Content.Length,
                    TracedReqIds = a.TracedRequirementIds, GeneratedAt = a.GeneratedAt
                }));

                db.SaveTestDiagnostics(context.RunId, context.TestDiagnostics.Select(d => new TestDiagRow
                {
                    Id = d.Id, TestName = d.TestName, AgentUnderTest = d.AgentUnderTest,
                    Outcome = d.Outcome.ToString(), Diagnostic = d.Diagnostic,
                    Remediation = d.Remediation, Category = d.Category,
                    DurationMs = d.DurationMs, AttemptNumber = d.AttemptNumber, Timestamp = d.Timestamp
                }));

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
                }, appCt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline background execution failed");
                try { db.FailRun(stateStore.CurrentSnapshot?.RunId ?? "unknown", ex.Message); } catch { }
            }
        }, appCt);

        return Accepted(new { status = "started", message = "Pipeline running. Poll GET /api/pipeline/status for progress." });
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is not null)
        {
            return Ok(new
            {
                status = "active",
                runId = ctx.RunId,
                agentStatuses = ctx.AgentStatuses.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.ToString()),
                requirements = ctx.Requirements.Count,
                artifacts = ctx.Artifacts.Count,
                findings = ctx.Findings.Count,
                completed = ctx.CompletedAt.HasValue
            });
        }

        // No active run — try DB for disaster recovery
        var latestRun = _db.GetLatestRun();
        if (latestRun is not null)
        {
            var agentRows = _db.GetAgentStatuses(latestRun.RunId);
            return Ok(new
            {
                status = "restored",
                runId = latestRun.RunId,
                agentStatuses = agentRows.ToDictionary(kv => kv.Key, kv => kv.Value.Status),
                agentMessages = agentRows.ToDictionary(kv => kv.Key, kv => kv.Value.Message),
                agentElapsed = agentRows.ToDictionary(kv => kv.Key, kv => kv.Value.ElapsedMs),
                agentArtifacts = agentRows.Where(kv => kv.Value.ArtifactCount > 0).ToDictionary(kv => kv.Key, kv => kv.Value.ArtifactCount),
                agentFindings = agentRows.Where(kv => kv.Value.FindingCount > 0).ToDictionary(kv => kv.Key, kv => kv.Value.FindingCount),
                agentRetries = agentRows.Where(kv => kv.Value.RetryAttempt > 0).ToDictionary(kv => kv.Key, kv => kv.Value.RetryAttempt),
                requirements = latestRun.RequirementCount,
                artifacts = latestRun.ArtifactCount,
                findings = latestRun.FindingCount,
                testDiagnostics = latestRun.TestDiagCount,
                backlog = latestRun.BacklogCount,
                completed = latestRun.Status == "Completed",
                durationMs = latestRun.DurationMs,
                source = "database"
            });
        }

        return Ok(new { status = "no_run" });
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

    [HttpPost("instruct")]
    public IActionResult SendInstruction([FromBody] InstructionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Instruction))
            return BadRequest(new { error = "Instruction text is required." });

        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null)
            return Ok(new { accepted = false, reason = "No active pipeline run." });

        ctx.OrchestratorInstructions.Add(request.Instruction);

        // Persist mid-pipeline instruction to DB
        _db.SaveInstruction(ctx.RunId, request.Instruction, "MidPipeline");

        _logger.LogInformation("Instruction received mid-pipeline: {Instruction}", request.Instruction);
        return Ok(new { accepted = true, queuedCount = ctx.OrchestratorInstructions.Count });
    }

    [HttpGet("instructions")]
    public IActionResult GetInstructions()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null) return Ok(Array.Empty<string>());
        return Ok(ctx.OrchestratorInstructions);
    }

    [HttpGet("instructions/history")]
    public IActionResult GetInstructionHistory([FromQuery] int limit = 50)
    {
        var rows = _db.GetInstructionHistory(limit);
        return Ok(new { instructions = rows, count = rows.Count });
    }

    [HttpPost("instructions/save")]
    public IActionResult SaveInstructionManually([FromBody] InstructionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Instruction))
            return BadRequest(new { error = "Instruction text is required." });
        _db.SaveInstruction(null, request.Instruction, "Saved");
        return Ok(new { saved = true });
    }

    // ─── Requirement Submission (mid-pipeline) ──────────────────────

    [HttpPost("requirements")]
    public async Task<IActionResult> SubmitRequirements(
        [FromBody] SubmitRequirementsRequest request,
        [FromServices] IHostApplicationLifetime lifetime)
    {
        if (request.Requirements is null || request.Requirements.Count == 0)
            return BadRequest(new { error = "At least one requirement is required." });

        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null)
            return Ok(new { accepted = false, reason = "No active pipeline run. Start a pipeline first." });

        var reqs = request.Requirements.Select((r, i) => new Requirement
        {
            Id = $"USER-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{i + 1:D3}",
            SourceFile = "user-submitted",
            Title = r.Title ?? string.Empty,
            Description = r.Description ?? string.Empty,
            Module = r.Module ?? "General",
            Tags = r.Tags ?? [],
            AcceptanceCriteria = r.AcceptanceCriteria ?? [],
            HeadingLevel = 2
        }).ToList();

        var appCt = lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.AddRequirementsAsync(reqs, appCt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process submitted requirements");
            }
        }, appCt);

        return Ok(new
        {
            accepted = true,
            count = reqs.Count,
            ids = reqs.Select(r => r.Id).ToList(),
            message = $"Submitted {reqs.Count} requirements — orchestrator will expand to backlog and dispatch agents."
        });
    }

    [HttpGet("backlog")]
    public IActionResult GetBacklog()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is not null && ctx.ExpandedRequirements.Count > 0)
        {
            return Ok(new
            {
                devIteration = ctx.DevIteration,
                totalItems = ctx.ExpandedRequirements.Count,
                items = ctx.ExpandedRequirements.Select(e => new
                {
                    e.Id,
                    e.ParentId,
                    e.SourceRequirementId,
                    itemType = e.ItemType.ToString(),
                    status = e.Status.ToString(),
                    e.Title,
                    e.Description,
                    e.Module,
                    e.Priority,
                    e.Iteration,
                    e.Tags,
                    e.AcceptanceCriteria,
                    e.CreatedAt,
                    e.StartedAt,
                    e.CompletedAt,
                    e.AssignedAgent
                }),
                stats = new
                {
                    epics = ctx.ExpandedRequirements.Count(e => e.ItemType == WorkItemType.Epic),
                    stories = ctx.ExpandedRequirements.Count(e => e.ItemType == WorkItemType.UserStory),
                    useCases = ctx.ExpandedRequirements.Count(e => e.ItemType == WorkItemType.UseCase),
                    tasks = ctx.ExpandedRequirements.Count(e => e.ItemType == WorkItemType.Task),
                    newCount = ctx.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.New),
                    inQueue = ctx.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.InQueue),
                    underDev = ctx.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.UnderDev),
                    completed = ctx.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Completed),
                    blocked = ctx.ExpandedRequirements.Count(e => e.Status == WorkItemStatus.Blocked),
                }
            });
        }

        // Fall back to DB
        var latestRun = _db.GetLatestRun();
        if (latestRun is not null)
        {
            var items = _db.GetBacklogItems(latestRun.RunId);
            if (items.Count > 0)
            {
                return Ok(new
                {
                    devIteration = 0,
                    totalItems = items.Count,
                    items = items.Select(e => new
                    {
                        e.Id,
                        e.ParentId,
                        e.SourceRequirementId,
                        itemType = e.ItemType,
                        status = e.Status,
                        e.Title,
                        e.Description,
                        e.Module,
                        e.Priority,
                        e.Iteration,
                        e.Tags,
                        e.AcceptanceCriteria,
                        e.CreatedAt,
                        e.StartedAt,
                        e.CompletedAt,
                        e.AssignedAgent
                    }),
                    stats = new
                    {
                        epics = items.Count(e => e.ItemType == "Epic"),
                        stories = items.Count(e => e.ItemType == "UserStory"),
                        useCases = items.Count(e => e.ItemType == "UseCase"),
                        tasks = items.Count(e => e.ItemType == "Task"),
                        newCount = items.Count(e => e.Status == "New"),
                        inQueue = items.Count(e => e.Status == "InQueue"),
                        underDev = items.Count(e => e.Status == "UnderDev"),
                        completed = items.Count(e => e.Status == "Completed"),
                        blocked = items.Count(e => e.Status == "Blocked"),
                    }
                });
            }
        }

        return Ok(new { totalItems = 0, items = Array.Empty<object>(), stats = new { epics = 0, stories = 0, useCases = 0, tasks = 0, newCount = 0, inQueue = 0, underDev = 0, completed = 0, blocked = 0 } });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Audit Log Endpoints
    // ═══════════════════════════════════════════════════════════════

    [HttpGet("audit")]
    public IActionResult GetAuditLog([FromQuery] string? runId = null, [FromQuery] int? limit = null)
    {
        var targetRunId = runId;
        if (string.IsNullOrEmpty(targetRunId))
        {
            var ctx = _orchestrator.GetCurrentContext();
            targetRunId = ctx?.RunId ?? _db.GetLatestRun()?.RunId;
        }
        if (string.IsNullOrEmpty(targetRunId))
            return Ok(new { entries = Array.Empty<object>(), verified = true });

        var entries = _db.GetAuditLog(targetRunId, limit);
        var verification = _db.VerifyAuditChain(targetRunId);

        return Ok(new
        {
            runId = targetRunId,
            entries = entries.Select(e => new
            {
                e.Id, e.Sequence, e.Agent, e.Action, e.Severity,
                e.Description, e.Details, e.InputHash, e.OutputHash,
                e.Timestamp, e.PreviousHash, e.EntryHash
            }),
            totalEntries = entries.Count,
            verified = verification.IsValid,
            brokenAtSequence = verification.BrokenAtSequence
        });
    }

    [HttpGet("audit/verify")]
    public async Task<IActionResult> VerifyAuditChain([FromQuery] string? runId = null)
    {
        var targetRunId = runId;
        if (string.IsNullOrEmpty(targetRunId))
        {
            var ctx = _orchestrator.GetCurrentContext();
            targetRunId = ctx?.RunId ?? _db.GetLatestRun()?.RunId;
        }
        if (string.IsNullOrEmpty(targetRunId))
            return Ok(new { verified = true, message = "No runs found" });

        var (isValid, brokenAt) = await _audit.VerifyChainAsync(targetRunId);
        return Ok(new
        {
            runId = targetRunId,
            verified = isValid,
            brokenAtSequence = brokenAt,
            message = isValid ? "Audit chain integrity verified — no tampering detected" : $"Chain broken at sequence {brokenAt}"
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Human-in-the-Loop Decision Endpoints
    // ═══════════════════════════════════════════════════════════════

    [HttpGet("decisions")]
    public async Task<IActionResult> GetPendingDecisions()
    {
        var pending = await _humanGate.GetPendingRequestsAsync();
        return Ok(new
        {
            pending = pending.Select(d => new
            {
                d.Id, d.RunId,
                agent = d.RequestingAgent.ToString(),
                category = d.Category.ToString(),
                d.Title, d.Description, d.Details,
                decision = d.Decision.ToString(),
                d.RequestedAt,
                timeoutMinutes = d.Timeout.TotalMinutes
            }),
            count = pending.Count
        });
    }

    [HttpGet("decisions/history")]
    public async Task<IActionResult> GetDecisionHistory([FromQuery] string? runId = null)
    {
        var targetRunId = runId;
        if (string.IsNullOrEmpty(targetRunId))
        {
            var ctx = _orchestrator.GetCurrentContext();
            targetRunId = ctx?.RunId ?? _db.GetLatestRun()?.RunId;
        }
        if (string.IsNullOrEmpty(targetRunId))
            return Ok(new { decisions = Array.Empty<object>() });

        var decisions = await _humanGate.GetDecisionHistoryAsync(targetRunId);
        return Ok(new
        {
            runId = targetRunId,
            decisions = decisions.Select(d => new
            {
                d.Id, d.RunId,
                agent = d.RequestingAgent.ToString(),
                category = d.Category.ToString(),
                d.Title, d.Description, d.Details,
                decision = d.Decision.ToString(),
                d.DecisionReason,
                d.RequestedAt, d.DecidedAt,
                timeoutMinutes = d.Timeout.TotalMinutes
            })
        });
    }

    [HttpPost("decisions/{id}/approve")]
    public async Task<IActionResult> ApproveDecision(string id, [FromBody] DecisionResponse? response = null)
    {
        await _humanGate.SubmitDecisionAsync(id, approved: true, response?.Reason);
        return Ok(new { id, decision = "Approved", reason = response?.Reason });
    }

    [HttpPost("decisions/{id}/reject")]
    public async Task<IActionResult> RejectDecision(string id, [FromBody] DecisionResponse? response = null)
    {
        await _humanGate.SubmitDecisionAsync(id, approved: false, response?.Reason);
        return Ok(new { id, decision = "Rejected", reason = response?.Reason });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Run History Endpoints
    // ═══════════════════════════════════════════════════════════════

    [HttpGet("history")]
    public IActionResult GetRunHistory()
    {
        var runs = _db.GetAllRuns();
        return Ok(new
        {
            runs = runs.Select(r => new
            {
                r.RunId, r.Status, r.StartedAt, r.CompletedAt,
                r.RequirementCount, r.ArtifactCount, r.FindingCount,
                r.TestDiagCount, r.BacklogCount, r.DurationMs
            }),
            totalRuns = runs.Count
        });
    }

    [HttpGet("run/{runId}")]
    public IActionResult GetRunDetails(string runId)
    {
        var run = _db.GetAllRuns().FirstOrDefault(r => r.RunId == runId);
        if (run is null) return NotFound(new { error = $"Run {runId} not found" });

        return Ok(new
        {
            run = new { run.RunId, run.Status, run.StartedAt, run.CompletedAt, run.RequirementCount, run.ArtifactCount, run.FindingCount, run.TestDiagCount, run.BacklogCount, run.DurationMs },
            agentStatuses = _db.GetAgentStatuses(runId),
            artifacts = _db.GetArtifacts(runId).Count,
            findings = _db.GetFindings(runId).Count,
            backlog = _db.GetBacklogItems(runId).Count,
            auditEntries = _db.GetAuditLog(runId).Count,
            events = _db.GetAgentEvents(runId).Count
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deploy Agent — On-Demand Deployment
    // ═══════════════════════════════════════════════════════════════

    [HttpPost("deploy")]
    public IActionResult DeployProject(
        [FromBody] DeployRequest request,
        [FromServices] IHostApplicationLifetime lifetime)
    {
        var outputPath = request.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            return BadRequest(new { error = "outputPath is required." });
        if (!Directory.Exists(outputPath))
            return BadRequest(new { error = $"Output path not found: {outputPath}" });

        var pipelineConfig = new PipelineConfig
        {
            RequirementsPath = request.RequirementsPath ?? string.Empty,
            OutputPath = outputPath,
            SolutionNamespace = request.SolutionNamespace ?? "Hms",
            DbConnectionString = string.Empty,
            SpinUpDocker = request.SpinUpDocker,
            ExecuteDdl = false,
            ServicePorts = request.ServicePorts ?? new ServicePortMap()
        };

        var appCt = lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Deploy triggered for: {Path}", outputPath);
                await _hub.Clients.All.SendAsync("PipelineEvent", new
                {
                    agent = (int)AgentType.Deploy,
                    status = (int)AgentStatus.Running,
                    message = $"Deployment started for {outputPath}",
                    artifactCount = 0,
                    findingCount = 0,
                    elapsedMs = 0.0,
                    retryAttempt = 0,
                    runId = "deploy-" + Guid.NewGuid().ToString("N")[..8]
                }, appCt);

                var context = await _orchestrator.RunSingleAgentAsync(pipelineConfig, AgentType.Deploy, appCt);

                var deployStatus = context.AgentStatuses.GetValueOrDefault(AgentType.Deploy, AgentStatus.Idle);
                await _hub.Clients.All.SendAsync("DeployComplete", new
                {
                    success = deployStatus == AgentStatus.Completed,
                    status = deployStatus.ToString(),
                    artifactCount = context.Artifacts.Count(a => a.ProducedBy == AgentType.Deploy),
                    outputPath
                }, appCt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deploy failed for {Path}", outputPath);
                await _hub.Clients.All.SendAsync("DeployComplete", new
                {
                    success = false,
                    status = "Failed",
                    error = ex.Message,
                    outputPath
                }, appCt);
            }
        }, appCt);

        return Accepted(new { message = "Deployment started", outputPath });
    }

    [HttpGet("deploy/status")]
    public IActionResult GetDeployStatus()
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null)
            return Ok(new { status = "no_context" });

        var deployStatus = ctx.AgentStatuses.GetValueOrDefault(AgentType.Deploy, AgentStatus.Idle);
        var deployArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Deploy).ToList();

        return Ok(new
        {
            status = deployStatus.ToString(),
            artifactCount = deployArtifacts.Count,
            report = deployArtifacts.FirstOrDefault(a => a.FileName == "deployment-report.md")?.Content
        });
    }
}

public sealed class DecisionResponse
{
    public string? Reason { get; init; }
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
    public string? OrchestratorInstructions { get; init; }
    public ServicePortMap? ServicePorts { get; init; }
}

public sealed class InstructionRequest
{
    public string Instruction { get; init; } = string.Empty;
}

public sealed class SubmitRequirementsRequest
{
    public List<RequirementInput> Requirements { get; init; } = [];
}

public sealed class RequirementInput
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Module { get; init; }
    public List<string>? Tags { get; init; }
    public List<string>? AcceptanceCriteria { get; init; }
}

public sealed class DeployRequest
{
    public string OutputPath { get; init; } = string.Empty;
    public string? RequirementsPath { get; init; }
    public string? SolutionNamespace { get; init; }
    public bool SpinUpDocker { get; init; } = true;
    public ServicePortMap? ServicePorts { get; init; }
}
