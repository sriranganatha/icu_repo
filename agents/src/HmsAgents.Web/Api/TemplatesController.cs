using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.AspNetCore.Mvc;

namespace HmsAgents.Web.Api;

[ApiController]
[Route("api/templates")]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateService _svc;

    public TemplatesController(ITemplateService svc) => _svc = svc;

    // ─── BRD Templates ──────────────────────────────────────
    [HttpGet("brd")]
    public async Task<IActionResult> ListBrdTemplates(CancellationToken ct)
        => Ok(await _svc.ListBrdTemplatesAsync(ct));

    [HttpPost("brd")]
    public async Task<IActionResult> CreateBrdTemplate([FromBody] CreateBrdTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateBrdTemplateAsync(request, ct));

    [HttpDelete("brd/{id}")]
    public async Task<IActionResult> DeleteBrdTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteBrdTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── Architecture Templates ─────────────────────────────
    [HttpGet("architecture")]
    public async Task<IActionResult> ListArchitectureTemplates(CancellationToken ct)
        => Ok(await _svc.ListArchitectureTemplatesAsync(ct));

    [HttpPost("architecture")]
    public async Task<IActionResult> CreateArchitectureTemplate([FromBody] CreateArchitectureTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateArchitectureTemplateAsync(request, ct));

    [HttpDelete("architecture/{id}")]
    public async Task<IActionResult> DeleteArchitectureTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteArchitectureTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── Code Templates ─────────────────────────────────────
    [HttpGet("code")]
    public async Task<IActionResult> ListCodeTemplates([FromQuery] string? languageId = null, [FromQuery] string? frameworkId = null, CancellationToken ct = default)
        => Ok(await _svc.ListCodeTemplatesAsync(languageId, frameworkId, ct));

    [HttpPost("code")]
    public async Task<IActionResult> CreateCodeTemplate([FromBody] CreateCodeTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateCodeTemplateAsync(request, ct));

    [HttpDelete("code/{id}")]
    public async Task<IActionResult> DeleteCodeTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteCodeTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── File Structure Templates ───────────────────────────
    [HttpGet("file-structure")]
    public async Task<IActionResult> ListFileStructureTemplates([FromQuery] string? frameworkId = null, CancellationToken ct = default)
        => Ok(await _svc.ListFileStructureTemplatesAsync(frameworkId, ct));

    [HttpPost("file-structure")]
    public async Task<IActionResult> CreateFileStructureTemplate([FromBody] CreateFileStructureTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateFileStructureTemplateAsync(request, ct));

    [HttpDelete("file-structure/{id}")]
    public async Task<IActionResult> DeleteFileStructureTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteFileStructureTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── CI/CD Templates ────────────────────────────────────
    [HttpGet("cicd")]
    public async Task<IActionResult> ListCiCdTemplates([FromQuery] string? provider = null, CancellationToken ct = default)
        => Ok(await _svc.ListCiCdTemplatesAsync(provider, ct));

    [HttpPost("cicd")]
    public async Task<IActionResult> CreateCiCdTemplate([FromBody] CreateCiCdTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateCiCdTemplateAsync(request, ct));

    [HttpDelete("cicd/{id}")]
    public async Task<IActionResult> DeleteCiCdTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteCiCdTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── Docker Templates ───────────────────────────────────
    [HttpGet("docker")]
    public async Task<IActionResult> ListDockerTemplates([FromQuery] string? frameworkId = null, CancellationToken ct = default)
        => Ok(await _svc.ListDockerTemplatesAsync(frameworkId, ct));

    [HttpPost("docker")]
    public async Task<IActionResult> CreateDockerTemplate([FromBody] CreateDockerTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateDockerTemplateAsync(request, ct));

    [HttpDelete("docker/{id}")]
    public async Task<IActionResult> DeleteDockerTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteDockerTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── Test Templates ─────────────────────────────────────
    [HttpGet("test")]
    public async Task<IActionResult> ListTestTemplates([FromQuery] string? frameworkId = null, CancellationToken ct = default)
        => Ok(await _svc.ListTestTemplatesAsync(frameworkId, ct));

    [HttpPost("test")]
    public async Task<IActionResult> CreateTestTemplate([FromBody] CreateTestTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateTestTemplateAsync(request, ct));

    [HttpDelete("test/{id}")]
    public async Task<IActionResult> DeleteTestTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteTestTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── IaC Templates ──────────────────────────────────────
    [HttpGet("iac")]
    public async Task<IActionResult> ListIaCTemplates([FromQuery] string? tool = null, CancellationToken ct = default)
        => Ok(await _svc.ListIaCTemplatesAsync(tool, ct));

    [HttpPost("iac")]
    public async Task<IActionResult> CreateIaCTemplate([FromBody] CreateIaCTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateIaCTemplateAsync(request, ct));

    [HttpDelete("iac/{id}")]
    public async Task<IActionResult> DeleteIaCTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteIaCTemplateAsync(id, ct);
        return NoContent();
    }

    // ─── Documentation Templates ────────────────────────────
    [HttpGet("documentation")]
    public async Task<IActionResult> ListDocumentationTemplates([FromQuery] string? docType = null, CancellationToken ct = default)
        => Ok(await _svc.ListDocumentationTemplatesAsync(docType, ct));

    [HttpPost("documentation")]
    public async Task<IActionResult> CreateDocumentationTemplate([FromBody] CreateDocumentationTemplateRequest request, CancellationToken ct)
        => Created("", await _svc.CreateDocumentationTemplateAsync(request, ct));

    [HttpDelete("documentation/{id}")]
    public async Task<IActionResult> DeleteDocumentationTemplate(string id, CancellationToken ct)
    {
        await _svc.DeleteDocumentationTemplateAsync(id, ct);
        return NoContent();
    }
}
