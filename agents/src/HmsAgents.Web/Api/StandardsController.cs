using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/standards")]
public class StandardsController : ControllerBase
{
    private readonly IStandardsService _svc;

    public StandardsController(IStandardsService svc) => _svc = svc;

    // ─── Coding Standards ───────────────────────────────────
    [HttpGet("coding")]
    public async Task<IActionResult> ListCodingStandards([FromQuery] string? languageId = null, CancellationToken ct = default)
        => Ok(await _svc.ListCodingStandardsAsync(languageId, ct));

    [HttpGet("coding/{id}")]
    public async Task<IActionResult> GetCodingStandard(string id, CancellationToken ct)
    {
        var dto = await _svc.GetCodingStandardAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("coding")]
    public async Task<IActionResult> CreateCodingStandard([FromBody] CreateCodingStandardRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateCodingStandardAsync(request, ct);
        return CreatedAtAction(nameof(GetCodingStandard), new { id = dto.Id }, dto);
    }

    [HttpPut("coding/{id}")]
    public async Task<IActionResult> UpdateCodingStandard(string id, [FromBody] UpdateCodingStandardRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route id must match body id");
        var dto = await _svc.UpdateCodingStandardAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("coding/{id}")]
    public async Task<IActionResult> DeleteCodingStandard(string id, CancellationToken ct)
    {
        await _svc.DeleteCodingStandardAsync(id, ct);
        return NoContent();
    }

    // ─── Naming Conventions ─────────────────────────────────
    [HttpGet("naming-conventions")]
    public async Task<IActionResult> ListNamingConventions(CancellationToken ct)
        => Ok(await _svc.ListNamingConventionsAsync(ct));

    [HttpPost("naming-conventions")]
    public async Task<IActionResult> CreateNamingConvention([FromBody] CreateNamingConventionRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateNamingConventionAsync(request, ct);
        return Created($"api/standards/naming-conventions/{dto.Id}", dto);
    }

    [HttpDelete("naming-conventions/{id}")]
    public async Task<IActionResult> DeleteNamingConvention(string id, CancellationToken ct)
    {
        await _svc.DeleteNamingConventionAsync(id, ct);
        return NoContent();
    }

    // ─── Quality Gates ──────────────────────────────────────
    [HttpGet("quality-gates")]
    public async Task<IActionResult> ListQualityGates(CancellationToken ct)
        => Ok(await _svc.ListQualityGatesAsync(ct));

    [HttpGet("quality-gates/{id}")]
    public async Task<IActionResult> GetQualityGate(string id, CancellationToken ct)
    {
        var dto = await _svc.GetQualityGateAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("quality-gates")]
    public async Task<IActionResult> CreateQualityGate([FromBody] CreateQualityGateRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateQualityGateAsync(request, ct);
        return CreatedAtAction(nameof(GetQualityGate), new { id = dto.Id }, dto);
    }

    [HttpPut("quality-gates/{id}")]
    public async Task<IActionResult> UpdateQualityGate(string id, [FromBody] UpdateQualityGateRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route id must match body id");
        var dto = await _svc.UpdateQualityGateAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("quality-gates/{id}")]
    public async Task<IActionResult> DeleteQualityGate(string id, CancellationToken ct)
    {
        await _svc.DeleteQualityGateAsync(id, ct);
        return NoContent();
    }

    // ─── Review Checklists ──────────────────────────────────
    [HttpGet("review-checklists")]
    public async Task<IActionResult> ListReviewChecklists([FromQuery] string? scope = null, CancellationToken ct = default)
        => Ok(await _svc.ListReviewChecklistsAsync(scope, ct));

    [HttpPost("review-checklists")]
    public async Task<IActionResult> CreateReviewChecklist([FromBody] CreateReviewChecklistRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateReviewChecklistAsync(request, ct);
        return Created($"api/standards/review-checklists/{dto.Id}", dto);
    }

    [HttpDelete("review-checklists/{id}")]
    public async Task<IActionResult> DeleteReviewChecklist(string id, CancellationToken ct)
    {
        await _svc.DeleteReviewChecklistAsync(id, ct);
        return NoContent();
    }

    // ─── Security Policies ──────────────────────────────────
    [HttpGet("security-policies")]
    public async Task<IActionResult> ListSecurityPolicies([FromQuery] string? category = null, CancellationToken ct = default)
        => Ok(await _svc.ListSecurityPoliciesAsync(category, ct));

    [HttpGet("security-policies/{id}")]
    public async Task<IActionResult> GetSecurityPolicy(string id, CancellationToken ct)
    {
        var dto = await _svc.GetSecurityPolicyAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("security-policies")]
    public async Task<IActionResult> CreateSecurityPolicy([FromBody] CreateSecurityPolicyRequest request, CancellationToken ct)
    {
        var dto = await _svc.CreateSecurityPolicyAsync(request, ct);
        return CreatedAtAction(nameof(GetSecurityPolicy), new { id = dto.Id }, dto);
    }

    [HttpPut("security-policies/{id}")]
    public async Task<IActionResult> UpdateSecurityPolicy(string id, [FromBody] UpdateSecurityPolicyRequest request, CancellationToken ct)
    {
        if (id != request.Id) return BadRequest("Route id must match body id");
        var dto = await _svc.UpdateSecurityPolicyAsync(request, ct);
        return Ok(dto);
    }

    [HttpDelete("security-policies/{id}")]
    public async Task<IActionResult> DeleteSecurityPolicy(string id, CancellationToken ct)
    {
        await _svc.DeleteSecurityPolicyAsync(id, ct);
        return NoContent();
    }
}
