using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectManagementService _svc;

    public ProjectsController(IProjectManagementService svc) => _svc = svc;

    // ── Projects ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListAsync(status, skip, take, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
    {
        var dto = await _svc.GetDetailAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest req, CancellationToken ct = default)
    {
        var dto = await _svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateProjectRequest req, CancellationToken ct = default)
    {
        if (id != req.Id) return BadRequest("ID mismatch");
        return Ok(await _svc.UpdateAsync(req, ct));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Archive(string id, CancellationToken ct = default)
    {
        await _svc.ArchiveAsync(id, ct);
        return NoContent();
    }

    // ── Tech Stack ────────────────────────────────────────
    [HttpPost("{id}/tech-stack")]
    public async Task<IActionResult> AddTechStack(string id, [FromBody] AddTechStackRequest req, CancellationToken ct = default)
    {
        if (id != req.ProjectId) return BadRequest("Project ID mismatch");
        return Created("", await _svc.AddTechStackAsync(req, ct));
    }

    [HttpDelete("tech-stack/{techStackId}")]
    public async Task<IActionResult> RemoveTechStack(string techStackId, CancellationToken ct = default)
    {
        await _svc.RemoveTechStackAsync(techStackId, ct);
        return NoContent();
    }

    // ── Epics ─────────────────────────────────────────────
    [HttpGet("{id}/epics")]
    public async Task<IActionResult> ListEpics(string id, CancellationToken ct = default)
        => Ok(await _svc.ListEpicsAsync(id, ct));

    [HttpPost("{id}/epics")]
    public async Task<IActionResult> CreateEpic(string id, [FromBody] CreateEpicRequest req, CancellationToken ct = default)
    {
        if (id != req.ProjectId) return BadRequest("Project ID mismatch");
        return Created("", await _svc.CreateEpicAsync(req, ct));
    }

    // ── Stories ───────────────────────────────────────────
    [HttpGet("epics/{epicId}/stories")]
    public async Task<IActionResult> ListStories(string epicId, CancellationToken ct = default)
        => Ok(await _svc.ListStoriesAsync(epicId, ct));

    [HttpPost("epics/{epicId}/stories")]
    public async Task<IActionResult> CreateStory(string epicId, [FromBody] CreateStoryRequest req, CancellationToken ct = default)
    {
        if (epicId != req.EpicId) return BadRequest("Epic ID mismatch");
        return Created("", await _svc.CreateStoryAsync(req, ct));
    }

    // ── Tasks ─────────────────────────────────────────────
    [HttpGet("stories/{storyId}/tasks")]
    public async Task<IActionResult> ListTasks(string storyId, CancellationToken ct = default)
        => Ok(await _svc.ListTasksAsync(storyId, ct));

    [HttpPost("stories/{storyId}/tasks")]
    public async Task<IActionResult> CreateTask(string storyId, [FromBody] CreateTaskItemRequest req, CancellationToken ct = default)
    {
        if (storyId != req.StoryId) return BadRequest("Story ID mismatch");
        return Created("", await _svc.CreateTaskItemAsync(req, ct));
    }

    // ── Sprints ───────────────────────────────────────────
    [HttpGet("{id}/sprints")]
    public async Task<IActionResult> ListSprints(string id, CancellationToken ct = default)
        => Ok(await _svc.ListSprintsAsync(id, ct));

    [HttpPost("{id}/sprints")]
    public async Task<IActionResult> CreateSprint(string id, [FromBody] CreateSprintRequest req, CancellationToken ct = default)
    {
        if (id != req.ProjectId) return BadRequest("Project ID mismatch");
        return Created("", await _svc.CreateSprintAsync(req, ct));
    }

    // ── Metrics ───────────────────────────────────────────
    [HttpGet("{id}/quality-reports")]
    public async Task<IActionResult> ListQualityReports(string id, CancellationToken ct = default)
        => Ok(await _svc.ListQualityReportsAsync(id, ct));

    [HttpGet("{id}/metrics")]
    public async Task<IActionResult> ListMetrics(string id, [FromQuery] string? metricType, CancellationToken ct = default)
        => Ok(await _svc.ListMetricsAsync(id, metricType, ct));
}
