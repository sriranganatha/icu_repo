using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/agent-registry")]
public sealed class AgentRegistryController : ControllerBase
{
    private readonly IAgentRegistryService _svc;

    public AgentRegistryController(IAgentRegistryService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListAsync(skip, take, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
    {
        var dto = await _svc.GetByIdAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("by-code/{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken ct = default)
    {
        var dto = await _svc.GetByCodeAsync(code, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAgentTypeRequest req, CancellationToken ct = default)
    {
        var dto = await _svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateAgentTypeRequest req, CancellationToken ct = default)
    {
        if (id != req.Id) return BadRequest("ID mismatch");
        return Ok(await _svc.UpdateAsync(req, ct));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }

    // Model Mappings
    [HttpPost("{id}/model-mappings")]
    public async Task<IActionResult> AddModelMapping(string id, [FromBody] CreateAgentModelMappingRequest req, CancellationToken ct = default)
    {
        if (id != req.AgentTypeDefinitionId) return BadRequest("ID mismatch");
        return Created("", await _svc.AddModelMappingAsync(req, ct));
    }

    // Prompts
    [HttpGet("{id}/prompts")]
    public async Task<IActionResult> ListPrompts(string id, CancellationToken ct = default)
        => Ok(await _svc.ListPromptsAsync(id, ct));

    [HttpPost("{id}/prompts")]
    public async Task<IActionResult> AddPrompt(string id, [FromBody] CreateAgentPromptRequest req, CancellationToken ct = default)
    {
        if (id != req.AgentTypeDefinitionId) return BadRequest("ID mismatch");
        return Created("", await _svc.AddPromptAsync(req, ct));
    }
}
