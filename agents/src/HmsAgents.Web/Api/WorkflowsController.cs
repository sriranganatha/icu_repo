using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _svc;

    public WorkflowsController(IWorkflowService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListWorkflowsAsync(skip, take, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var dto = await _svc.GetWorkflowAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateWorkflowAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateWorkflowRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route id must match body id");
        var dto = await _svc.UpdateWorkflowAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _svc.DeleteWorkflowAsync(id, ct);
        return NoContent();
    }

    // ─── Stages ─────────────────────────────────────────────
    [HttpPost("{workflowId}/stages")]
    public async Task<IActionResult> AddStage(string workflowId, [FromBody] CreateStageRequest request, CancellationToken ct)
    {
        if (workflowId != request.WorkflowId) return BadRequest("Route workflowId must match body");
        var dto = await _svc.AddStageAsync(request, ct);
        return Created($"api/workflows/{workflowId}/stages/{dto.Id}", dto);
    }

    [HttpPut("{workflowId}/stages/{stageId}")]
    public async Task<IActionResult> UpdateStage(string workflowId, string stageId, [FromBody] UpdateStageRequest request, CancellationToken ct)
    {
        if (stageId != request.Id) return BadRequest("Route stageId must match body id");
        var dto = await _svc.UpdateStageAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("{workflowId}/stages/{stageId}")]
    public async Task<IActionResult> DeleteStage(string workflowId, string stageId, CancellationToken ct)
    {
        await _svc.DeleteStageAsync(stageId, ct);
        return NoContent();
    }

    // ─── Approval Gates ─────────────────────────────────────
    [HttpPost("{workflowId}/stages/{stageId}/approval-gates")]
    public async Task<IActionResult> AddApprovalGate(string stageId, [FromBody] CreateApprovalGateRequest request, CancellationToken ct)
    {
        if (stageId != request.StageId) return BadRequest("Route stageId must match body");
        var dto = await _svc.AddApprovalGateAsync(request, ct);
        return Created("", dto);
    }

    [HttpDelete("{workflowId}/stages/{stageId}/approval-gates/{gateId}")]
    public async Task<IActionResult> DeleteApprovalGate(string gateId, CancellationToken ct)
    {
        await _svc.DeleteApprovalGateAsync(gateId, ct);
        return NoContent();
    }

    // ─── Transition Rules ───────────────────────────────────
    [HttpPost("{workflowId}/transition-rules")]
    public async Task<IActionResult> AddTransitionRule([FromBody] CreateTransitionRuleRequest request, CancellationToken ct)
    {
        var dto = await _svc.AddTransitionRuleAsync(request, ct);
        return Created("", dto);
    }

    [HttpDelete("{workflowId}/transition-rules/{ruleId}")]
    public async Task<IActionResult> DeleteTransitionRule(string ruleId, CancellationToken ct)
    {
        await _svc.DeleteTransitionRuleAsync(ruleId, ct);
        return NoContent();
    }
}
