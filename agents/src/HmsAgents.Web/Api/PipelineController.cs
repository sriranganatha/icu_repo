using System.Diagnostics;
using System.Text;
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
    private static readonly object s_runLock = new();
    private static CancellationTokenSource? s_runCts;
    private static bool s_runActive;

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

        CancellationTokenSource runCts;
        lock (s_runLock)
        {
            if (s_runActive)
                return Conflict(new { error = "A pipeline is already running. Stop it first or wait for completion." });

            s_runCts?.Dispose();
            s_runCts = new CancellationTokenSource();
            runCts = s_runCts;
            s_runActive = true;
        }

        // Fire-and-forget: use app lifetime token + stop token so UI can cancel active runs.
        var lifetimeCt = lifetime.ApplicationStopping;
        var stateStore = _stateStore;
        var db = _db;
        var configJson = JsonSerializer.Serialize(pipelineConfig);

        // Persist initial orchestrator instructions to DB
        if (!string.IsNullOrWhiteSpace(pipelineConfig.OrchestratorInstructions))
            _db.SaveInstruction(null, pipelineConfig.OrchestratorInstructions, "PipelineStart");

        _ = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCt, runCts.Token);
            var appCt = linked.Token;
            var sw = Stopwatch.StartNew();
            stateStore.Reset(Guid.NewGuid().ToString("N")[..8]);
            AgentContext? context = null;
            try
            {
                context = await _orchestrator.RunPipelineAsync(pipelineConfig, appCt);

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
                    TechnicalNotes = e.TechnicalNotes,
                    DefinitionOfDone = e.DefinitionOfDone,
                    DetailedSpec = e.DetailedSpec,
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
            catch (OperationCanceledException)
            {
                var reqCount = context?.Requirements.Count ?? 0;
                var artifactCount = context?.Artifacts.Count ?? 0;
                var findingCount = context?.Findings.Count ?? 0;
                var testDiagCount = context?.TestDiagnostics.Count ?? 0;

                if (!string.IsNullOrWhiteSpace(context?.RunId))
                {
                    try { db.FailRun(context.RunId, "Pipeline was stopped by user."); } catch { }
                }

                await _hub.Clients.All.SendAsync("PipelineComplete", new
                {
                    requirementCount = reqCount,
                    artifactCount,
                    findingCount,
                    testDiagnosticCount = testDiagCount,
                    durationMs = sw.Elapsed.TotalMilliseconds,
                    findings = Array.Empty<object>(),
                    artifacts = Array.Empty<object>(),
                    testDiagnostics = Array.Empty<object>(),
                    cancelled = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline background execution failed");
                try { db.FailRun(stateStore.CurrentSnapshot?.RunId ?? "unknown", ex.Message); } catch { }
            }
            finally
            {
                lock (s_runLock)
                {
                    s_runActive = false;
                    if (ReferenceEquals(s_runCts, runCts))
                    {
                        s_runCts.Dispose();
                        s_runCts = null;
                    }
                }
            }
        }, CancellationToken.None);

        return Accepted(new { status = "started", message = "Pipeline running. Poll GET /api/pipeline/status for progress." });
    }

    [HttpPost("stop")]
    public IActionResult StopPipeline()
    {
        lock (s_runLock)
        {
            if (!s_runActive || s_runCts is null)
                return Ok(new { stopped = false, message = "No active pipeline run." });

            if (!s_runCts.IsCancellationRequested)
                s_runCts.Cancel();
        }

        return Ok(new { stopped = true, message = "Stop requested. Active agents are being cancelled." });
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

    [HttpGet("artifacts/{id}")]
    public IActionResult GetArtifactContent(string id)
    {
        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null) return NotFound(new { error = "No active pipeline context" });

        var artifact = ctx.Artifacts.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (artifact is null)
            return NotFound(new { error = "Artifact not found", id });

        var fileName = string.IsNullOrWhiteSpace(artifact.FileName) ? $"{id}.txt" : artifact.FileName;
        return File(Encoding.UTF8.GetBytes(artifact.Content), "text/plain; charset=utf-8", fileName);
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

    // ─── Work Item Templates ───────────────────────────────────────

    [HttpGet("templates")]
    public IActionResult GetTemplates([FromQuery] bool activeOnly = false)
    {
        var rows = _db.GetWorkItemTemplates(activeOnly);
        return Ok(new
        {
            templates = rows.Select(t => new
            {
                t.TemplateKey,
                t.ItemType,
                t.TemplateName,
                t.Purpose,
                t.TemplateFormat,
                t.ExampleContent,
                t.Version,
                t.IsActive,
                t.UpdatedAt
            }),
            count = rows.Count
        });
    }

    [HttpPut("templates/{templateKey}")]
    public IActionResult UpsertTemplate(string templateKey, [FromBody] TemplateUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            return BadRequest(new { error = "templateKey is required" });
        if (string.IsNullOrWhiteSpace(dto.ItemType))
            return BadRequest(new { error = "itemType is required" });
        if (string.IsNullOrWhiteSpace(dto.TemplateName))
            return BadRequest(new { error = "templateName is required" });
        if (string.IsNullOrWhiteSpace(dto.Purpose))
            return BadRequest(new { error = "purpose is required" });
        if (string.IsNullOrWhiteSpace(dto.TemplateFormat))
            return BadRequest(new { error = "templateFormat is required" });

        _db.UpsertWorkItemTemplate(new WorkItemTemplateRow
        {
            TemplateKey = templateKey,
            ItemType = dto.ItemType,
            TemplateName = dto.TemplateName,
            Purpose = dto.Purpose,
            TemplateFormat = dto.TemplateFormat,
            ExampleContent = dto.ExampleContent,
            Version = dto.Version <= 0 ? 1 : dto.Version,
            IsActive = dto.IsActive
        });

        return Ok(new { saved = true, templateKey });
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
                    e.DependsOn,
                    e.TechnicalNotes,
                    e.DefinitionOfDone,
                    e.DetailedSpec,
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
                        e.DependsOn,
                        e.TechnicalNotes,
                        e.DefinitionOfDone,
                        e.DetailedSpec,
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

    /// <summary>Update priority, status, assignedAgent, or iteration for a single backlog item.</summary>
    [HttpPatch("backlog/{itemId}")]
    public IActionResult UpdateBacklogItem(string itemId, [FromBody] BacklogItemPatchDto patch)
    {
        // Resolve runId
        var ctx = _orchestrator.GetCurrentContext();
        var runId = ctx?.RunId ?? _db.GetLatestRun()?.RunId;
        if (string.IsNullOrEmpty(runId))
            return NotFound(new { error = "No pipeline run found" });

        // Validate status if provided
        string[]? validStatuses = ["New", "InQueue", "UnderDev", "Completed", "Blocked"];
        if (patch.Status is not null && !validStatuses.Contains(patch.Status))
            return BadRequest(new { error = $"Invalid status. Must be one of: {string.Join(", ", validStatuses)}" });

        // Update in-memory context if live
        if (ctx is not null)
        {
            var item = ctx.ExpandedRequirements.FirstOrDefault(e => e.Id == itemId);
            if (item is not null)
            {
                if (patch.Priority.HasValue) item.Priority = patch.Priority.Value;
                if (patch.Status is not null && Enum.TryParse<WorkItemStatus>(patch.Status, out var ws)) item.Status = ws;
                if (patch.AssignedAgent is not null) item.AssignedAgent = patch.AssignedAgent;
                if (patch.Iteration.HasValue) item.Iteration = patch.Iteration.Value;
            }
        }

        // Persist to DB
        var updated = _db.UpdateBacklogItem(runId, itemId, patch.Priority, patch.Status, patch.AssignedAgent, patch.Iteration);
        return updated
            ? Ok(new { success = true, itemId, runId })
            : NotFound(new { error = "Backlog item not found", itemId });
    }

    /// <summary>Bulk update priorities for multiple backlog items.</summary>
    [HttpPost("backlog/priorities")]
    public IActionResult BulkUpdatePriorities([FromBody] List<BacklogPriorityDto> items)
    {
        var ctx = _orchestrator.GetCurrentContext();
        var runId = ctx?.RunId ?? _db.GetLatestRun()?.RunId;
        if (string.IsNullOrEmpty(runId))
            return NotFound(new { error = "No pipeline run found" });

        var updatedCount = 0;
        foreach (var dto in items)
        {
            // Update in-memory
            if (ctx is not null)
            {
                var item = ctx.ExpandedRequirements.FirstOrDefault(e => e.Id == dto.ItemId);
                if (item is not null) item.Priority = dto.Priority;
            }
            // Update DB
            if (_db.UpdateBacklogPriority(runId, dto.ItemId, dto.Priority))
                updatedCount++;
        }
        return Ok(new { success = true, updatedCount, total = items.Count });
    }

    public sealed class BacklogItemPatchDto
    {
        public int? Priority { get; set; }
        public string? Status { get; set; }
        public string? AssignedAgent { get; set; }
        public int? Iteration { get; set; }
    }

    public sealed class BacklogPriorityDto
    {
        public string ItemId { get; set; } = "";
        public int Priority { get; set; }
    }

    public sealed class TemplateUpsertDto
    {
        public string ItemType { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string TemplateFormat { get; set; } = "";
        public string? ExampleContent { get; set; }
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
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

    // ── Ask Agent ────────────────────────────────────────────────────────
    [HttpPost("agent/ask")]
    public async Task<IActionResult> AskAgent(
        [FromBody] AskAgentRequest request,
        [FromServices] ILlmProvider llm,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AgentKey) || string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "agentKey and question are required." });

        if (!Enum.TryParse<AgentType>(request.AgentKey, ignoreCase: true, out var agentType))
            return BadRequest(new { error = $"Unknown agent: {request.AgentKey}" });

        var ctx = _orchestrator.GetCurrentContext();
        if (ctx is null)
            return Ok(new { answer = "No pipeline context available. Please run the pipeline first." });

        // Gather ONLY this agent's work context
        var artifacts = ctx.Artifacts
            .Where(a => a.ProducedBy == agentType)
            .Select(a => $"[{a.Layer}] {a.RelativePath}/{a.FileName} ({a.Content.Length} chars)")
            .ToList();

        var findings = ctx.Findings
            .Where(f => f.Category != null)
            .ToList();

        var backlogItems = ctx.ExpandedRequirements
            .Where(r => r.AssignedAgent?.Equals(agentType.ToString(), StringComparison.OrdinalIgnoreCase) == true
                     || r.ProducedBy?.Equals(agentType.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            .Select(r => $"[{r.Status}] {r.Title} (P{r.Priority})")
            .ToList();

        var agentStatus = ctx.AgentStatuses.GetValueOrDefault(agentType, AgentStatus.Idle);

        var messages = ctx.Messages
            .Where(m => m.From == agentType || m.To == agentType)
            .Select(m => $"{m.From}→{m.To}: {m.Subject} — {m.Body}")
            .Take(20)
            .ToList();

        // Build scoped context for the LLM
        var contextParts = new List<string>();
        contextParts.Add($"Agent: {agentType}, Status: {agentStatus}");

        if (artifacts.Count > 0)
            contextParts.Add($"Artifacts produced ({artifacts.Count}):\n" + string.Join("\n", artifacts.Take(50)));

        // Include artifact content snippets (first 500 chars each, up to 10 artifacts)
        var artifactContents = ctx.Artifacts
            .Where(a => a.ProducedBy == agentType)
            .Take(10)
            .Select(a => $"--- {a.FileName} ---\n{(a.Content.Length > 500 ? a.Content[..500] + "..." : a.Content)}")
            .ToList();
        if (artifactContents.Count > 0)
            contextParts.Add("Artifact content samples:\n" + string.Join("\n\n", artifactContents));

        if (findings.Count > 0)
            contextParts.Add($"Related findings ({findings.Count}):\n" + string.Join("\n",
                findings.Take(30).Select(f => $"[{f.Severity}] {f.Category}: {f.Message}")));

        if (backlogItems.Count > 0)
            contextParts.Add($"Backlog items ({backlogItems.Count}):\n" + string.Join("\n", backlogItems.Take(30)));

        if (messages.Count > 0)
            contextParts.Add($"Inter-agent messages:\n" + string.Join("\n", messages));

        var prompt = new LlmPrompt
        {
            SystemPrompt = $"""
                You are the {agentType} agent in an HMS (Hospital Management System) multi-agent pipeline.
                Answer the user's question ONLY based on the work context provided below.
                Be concise, factual, and specific. Reference actual file names, finding categories, or backlog items when relevant.
                If requested information is not in your context, say so clearly.
                Do NOT make up information. Scope is strictly limited to your agent's work.
                """,
            UserPrompt = request.Question,
            ContextSnippets = contextParts,
            Temperature = 0.1,
            MaxTokens = 2048,
            RequestingAgent = $"AskAgent-{agentType}"
        };

        var response = await llm.GenerateAsync(prompt, ct);

        if (!response.Success)
            return Ok(new { answer = $"LLM error: {response.Error ?? "unknown"}" });

        return Ok(new { answer = response.Content, model = response.Model, tokens = response.CompletionTokens });
    }

    // ─── Reset Project ─────────────────────────────────────────
    [HttpPost("reset")]
    public IActionResult ResetProject([FromBody] ResetRequest? request)
    {
        try
        {
            // 1. Clear in-memory context
            _orchestrator.ResetContext();

            // 2. Reset state store (in-memory + pipeline-state.json)
            _stateStore.Reset("reset");

            // 3. Purge all SQLite data
            _db.PurgeAllData();

            // 4. Optionally delete generated output directory
            var deleteOutput = request?.DeleteGeneratedCode ?? false;
            var outputDeleted = false;
            if (deleteOutput && !string.IsNullOrEmpty(request?.OutputPath) && Directory.Exists(request.OutputPath))
            {
                // Safety: only delete known subdirectories, not the root
                var srcDir = new DirectoryInfo(request.OutputPath);
                foreach (var sub in srcDir.GetDirectories("Hms.*"))
                {
                    sub.Delete(recursive: true);
                }
                // Also delete solution files generated by agents
                foreach (var f in srcDir.GetFiles("*.sln"))
                    f.Delete();
                outputDeleted = true;
            }

            _logger.LogWarning("PROJECT RESET performed. DB purged, context cleared, state reset. OutputDeleted={Del}", outputDeleted);

            return Ok(new { success = true, message = "Project reset complete. All pipeline data cleared.", outputDeleted });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reset failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public sealed class ResetRequest
{
    public bool DeleteGeneratedCode { get; init; }
    public string? OutputPath { get; init; }
}

public sealed class AskAgentRequest
{
    public string AgentKey { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
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
