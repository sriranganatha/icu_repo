using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/llm-config")]
public class LlmConfigController : ControllerBase
{
    private readonly ILlmConfigService _svc;

    public LlmConfigController(ILlmConfigService svc) => _svc = svc;

    // ─── Providers ──────────────────────────────────────────
    [HttpGet("providers")]
    public async Task<IActionResult> ListProviders([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListProvidersAsync(skip, take, ct));

    [HttpGet("providers/{id}")]
    public async Task<IActionResult> GetProvider(string id, CancellationToken ct)
    {
        var dto = await _svc.GetProviderAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("providers")]
    public async Task<IActionResult> CreateProvider([FromBody] CreateLlmProviderRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateProviderAsync(request, ct);
        return CreatedAtAction(nameof(GetProvider), new { id = dto.Id }, dto);
    }

    [HttpPut("providers/{id}")]
    public async Task<IActionResult> UpdateProvider(string id, [FromBody] UpdateLlmProviderRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route id must match body id");
        var dto = await _svc.UpdateProviderAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("providers/{id}")]
    public async Task<IActionResult> DeleteProvider(string id, CancellationToken ct)
    {
        await _svc.DeleteProviderAsync(id, ct);
        return NoContent();
    }

    // ─── Models ─────────────────────────────────────────────
    [HttpGet("models")]
    public async Task<IActionResult> ListModels([FromQuery] string? providerId = null, CancellationToken ct = default)
        => Ok(await _svc.ListModelsAsync(providerId, ct));

    [HttpPost("models")]
    public async Task<IActionResult> AddModel([FromBody] CreateLlmModelRequest request, CancellationToken ct)
    {
        var dto = await _svc.AddModelAsync(request, ct);
        return Created($"api/llm-config/models/{dto.Id}", dto);
    }

    [HttpPut("models/{id}")]
    public async Task<IActionResult> UpdateModel(string id, [FromBody] UpdateLlmModelRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route id must match body id");
        var dto = await _svc.UpdateModelAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("models/{id}")]
    public async Task<IActionResult> DeleteModel(string id, CancellationToken ct)
    {
        await _svc.DeleteModelAsync(id, ct);
        return NoContent();
    }

    // ─── Routing Rules ──────────────────────────────────────
    [HttpGet("routing-rules")]
    public async Task<IActionResult> ListRoutingRules(CancellationToken ct)
        => Ok(await _svc.ListRoutingRulesAsync(ct));

    [HttpPost("routing-rules")]
    public async Task<IActionResult> CreateRoutingRule([FromBody] CreateRoutingRuleRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateRoutingRuleAsync(request, ct);
        return Created($"api/llm-config/routing-rules/{dto.Id}", dto);
    }

    [HttpDelete("routing-rules/{id}")]
    public async Task<IActionResult> DeleteRoutingRule(string id, CancellationToken ct)
    {
        await _svc.DeleteRoutingRuleAsync(id, ct);
        return NoContent();
    }

    // ─── Token Budgets ──────────────────────────────────────
    [HttpGet("token-budgets")]
    public async Task<IActionResult> ListTokenBudgets([FromQuery] string? projectId = null, CancellationToken ct = default)
        => Ok(await _svc.ListTokenBudgetsAsync(projectId, ct));

    [HttpPost("token-budgets")]
    public async Task<IActionResult> CreateTokenBudget([FromBody] CreateTokenBudgetRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateTokenBudgetAsync(request, ct);
        return Created($"api/llm-config/token-budgets/{dto.Id}", dto);
    }

    [HttpDelete("token-budgets/{id}")]
    public async Task<IActionResult> DeleteTokenBudget(string id, CancellationToken ct)
    {
        await _svc.DeleteTokenBudgetAsync(id, ct);
        return NoContent();
    }
}
