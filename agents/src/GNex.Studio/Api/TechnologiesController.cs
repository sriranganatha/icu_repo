using GNex.Services.Dtos.Platform;
using GNex.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace GNex.Studio.Api;

[ApiController]
[Route("api/technologies")]
public sealed class TechnologiesController : ControllerBase
{
    private readonly ITechnologyService _svc;

    public TechnologiesController(ITechnologyService svc) => _svc = svc;

    // ── Languages ─────────────────────────────────────────
    [HttpGet("languages")]
    public async Task<IActionResult> ListLanguages([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListLanguagesAsync(skip, take, ct));

    [HttpGet("languages/{id}")]
    public async Task<IActionResult> GetLanguage(string id, CancellationToken ct = default)
    {
        var dto = await _svc.GetLanguageAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("languages")]
    public async Task<IActionResult> CreateLanguage([FromBody] CreateLanguageRequest req, CancellationToken ct = default)
    {
        var dto = await _svc.CreateLanguageAsync(req, ct);
        return CreatedAtAction(nameof(GetLanguage), new { id = dto.Id }, dto);
    }

    [HttpPut("languages/{id}")]
    public async Task<IActionResult> UpdateLanguage(string id, [FromBody] UpdateLanguageRequest req, CancellationToken ct = default)
    {
        if (id != req.Id) return BadRequest("ID mismatch");
        return Ok(await _svc.UpdateLanguageAsync(req, ct));
    }

    [HttpDelete("languages/{id}")]
    public async Task<IActionResult> DeleteLanguage(string id, CancellationToken ct = default)
    {
        await _svc.DeleteLanguageAsync(id, ct);
        return NoContent();
    }

    // ── Frameworks ────────────────────────────────────────
    [HttpGet("frameworks")]
    public async Task<IActionResult> ListFrameworks([FromQuery] string? languageId, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListFrameworksAsync(languageId, skip, take, ct));

    [HttpGet("frameworks/{id}")]
    public async Task<IActionResult> GetFramework(string id, CancellationToken ct = default)
    {
        var dto = await _svc.GetFrameworkAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("frameworks")]
    public async Task<IActionResult> CreateFramework([FromBody] CreateFrameworkRequest req, CancellationToken ct = default)
    {
        var dto = await _svc.CreateFrameworkAsync(req, ct);
        return CreatedAtAction(nameof(GetFramework), new { id = dto.Id }, dto);
    }

    [HttpDelete("frameworks/{id}")]
    public async Task<IActionResult> DeleteFramework(string id, CancellationToken ct = default)
    {
        await _svc.DeleteFrameworkAsync(id, ct);
        return NoContent();
    }

    // ── Databases ─────────────────────────────────────────
    [HttpGet("databases")]
    public async Task<IActionResult> ListDatabases(CancellationToken ct = default)
        => Ok(await _svc.ListDatabasesAsync(ct));

    [HttpPost("databases")]
    public async Task<IActionResult> CreateDatabase([FromBody] CreateDatabaseTechnologyRequest req, CancellationToken ct = default)
        => Created("", await _svc.CreateDatabaseAsync(req, ct));

    // ── Cloud Providers ───────────────────────────────────
    [HttpGet("cloud-providers")]
    public async Task<IActionResult> ListCloudProviders(CancellationToken ct = default)
        => Ok(await _svc.ListCloudProvidersAsync(ct));

    [HttpPost("cloud-providers")]
    public async Task<IActionResult> CreateCloudProvider([FromBody] CreateCloudProviderRequest req, CancellationToken ct = default)
        => Created("", await _svc.CreateCloudProviderAsync(req, ct));

    // ── DevOps Tools ──────────────────────────────────────
    [HttpGet("devops-tools")]
    public async Task<IActionResult> ListDevOpsTools(CancellationToken ct = default)
        => Ok(await _svc.ListDevOpsToolsAsync(ct));

    [HttpPost("devops-tools")]
    public async Task<IActionResult> CreateDevOpsTool([FromBody] CreateDevOpsToolRequest req, CancellationToken ct = default)
        => Created("", await _svc.CreateDevOpsToolAsync(req, ct));
}
