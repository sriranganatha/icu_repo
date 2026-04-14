using GNex.Services.Platform;
using GNex.Services.Dtos.Platform;
using Microsoft.AspNetCore.Mvc;

namespace GNex.Studio.Api;

[ApiController]
[Route("api/brd")]
public class BrdUploadController(IBrdUploadService svc, IBrdWorkflowService workflow) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = [".txt", ".md", ".csv", ".json", ".pdf", ".docx"];
    private const long MaxFileSize = 10 * 1024 * 1024;

    // ═══════════════════════════════════════════════════════════════
    // BRD Document CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Get all BRD documents, optionally filtered by project.</summary>
    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments([FromQuery] string? projectId, CancellationToken ct)
        => Ok(await svc.GetBrdDocumentsAsync(projectId, ct));

    /// <summary>Get a single BRD document by ID.</summary>
    [HttpGet("documents/{brdId}")]
    public async Task<IActionResult> GetDocument(string brdId, CancellationToken ct)
    {
        var doc = await svc.GetBrdDocumentAsync(brdId, ct);
        return doc is null ? NotFound(new { error = "BRD document not found." }) : Ok(doc);
    }

    /// <summary>Create one or more BRD documents (one per selected type).</summary>
    [HttpPost("documents")]
    public async Task<IActionResult> CreateDocuments([FromBody] CreateBrdDocumentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(new { error = "ProjectId is required." });
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required." });
        if (request.BrdTypes is null || request.BrdTypes.Count == 0)
            return BadRequest(new { error = "At least one BRD type must be selected." });

        foreach (var t in request.BrdTypes)
        {
            if (!BrdTypeTemplates.IsValid(t))
                return BadRequest(new { error = $"Invalid BRD type: {t}" });
        }

        var result = await svc.CreateBrdDocumentsAsync(request, ct);
        return Created($"/api/brd/documents?projectId={request.ProjectId}", result);
    }

    /// <summary>Update a BRD document's title, description, or instructions.</summary>
    [HttpPut("documents/{brdId}")]
    public async Task<IActionResult> UpdateDocument(string brdId, [FromBody] UpdateBrdDocumentRequest request, CancellationToken ct)
    {
        var ok = await svc.UpdateBrdDocumentAsync(brdId, request, ct);
        return ok ? Ok(new { message = "BRD document updated." }) : NotFound(new { error = "BRD document not found." });
    }

    /// <summary>Delete (soft) a BRD document and its sections.</summary>
    [HttpDelete("documents/{brdId}")]
    public async Task<IActionResult> DeleteDocument(string brdId, CancellationToken ct)
    {
        var ok = await svc.DeleteBrdDocumentAsync(brdId, ct);
        return ok ? Ok(new { message = "BRD document deleted." }) : NotFound(new { error = "BRD document not found." });
    }

    /// <summary>Get sibling BRD documents that share the same group.</summary>
    [HttpGet("documents/{brdId}/siblings")]
    public async Task<IActionResult> GetGroupSiblings(string brdId, CancellationToken ct)
    {
        var siblings = await svc.GetGroupSiblingsAsync(brdId, ct);
        return Ok(siblings);
    }

    // ═══════════════════════════════════════════════════════════════
    // BRD Sections
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Get sections for a BRD document.</summary>
    [HttpGet("documents/{brdId}/sections")]
    public async Task<IActionResult> GetSections(string brdId, CancellationToken ct)
        => Ok(await svc.GetBrdSectionsAsync(brdId, ct));

    /// <summary>Update a single section's content.</summary>
    [HttpPut("sections/{sectionId}")]
    public async Task<IActionResult> UpdateSection(string sectionId, [FromBody] UpdateSectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Content is required." });

        var ok = await svc.UpdateSectionAsync(sectionId, request.Content, ct);
        return ok ? Ok(new { message = "Section updated." }) : NotFound(new { error = "Section not found." });
    }

    /// <summary>Delete all sections for a BRD document.</summary>
    [HttpDelete("documents/{brdId}/sections")]
    public async Task<IActionResult> DeleteSections(string brdId, CancellationToken ct)
    {
        var count = await svc.DeleteSectionsAsync(brdId, ct);
        return Ok(new { count, message = $"{count} section(s) removed." });
    }

    // ═══════════════════════════════════════════════════════════════
    // AI Enrichment
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Enrich all draft sections for a BRD document using AI.</summary>
    [HttpPost("documents/{brdId}/enrich")]
    public async Task<IActionResult> EnrichBrd(string brdId, CancellationToken ct)
    {
        var result = await svc.EnrichBrdAsync(brdId, ct);
        return result.Status switch
        {
            "not_found" => NotFound(new { error = "BRD document not found." }),
            "no_sources" => BadRequest(new { error = "No source documents found for this project. Upload files first." }),
            _ => Ok(result)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Workflow (submit, approve, reject, request-changes)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Get BRD workflow status.</summary>
    [HttpGet("documents/{brdId}/status")]
    public async Task<IActionResult> GetStatus(string brdId, CancellationToken ct)
    {
        var result = await workflow.GetStatusAsync(brdId, ct);
        return result.Success ? Ok(result) : NotFound(new { error = result.Message });
    }

    /// <summary>Submit BRD for review.</summary>
    [HttpPost("documents/{brdId}/submit")]
    public async Task<IActionResult> SubmitForReview(string brdId, [FromBody] WorkflowActionRequest request, CancellationToken ct)
    {
        var result = await workflow.SubmitForReviewAsync(brdId, request.Reviewer ?? "user", ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message });
    }

    /// <summary>Approve BRD.</summary>
    [HttpPost("documents/{brdId}/approve")]
    public async Task<IActionResult> Approve(string brdId, [FromBody] WorkflowActionRequest request, CancellationToken ct)
    {
        var result = await workflow.ApproveAsync(brdId, request.Reviewer ?? "user", request.Comment, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message });
    }

    /// <summary>Reject BRD.</summary>
    [HttpPost("documents/{brdId}/reject")]
    public async Task<IActionResult> Reject(string brdId, [FromBody] WorkflowActionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Reason is required." });

        var result = await workflow.RejectAsync(brdId, request.Reviewer ?? "user", request.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message });
    }

    /// <summary>Request changes on BRD.</summary>
    [HttpPost("documents/{brdId}/request-changes")]
    public async Task<IActionResult> RequestChanges(string brdId, [FromBody] WorkflowActionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Feedback))
            return BadRequest(new { error = "Feedback is required." });

        var result = await workflow.RequestChangesAsync(brdId, request.Reviewer ?? "user", request.Feedback, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message });
    }

    // ═══════════════════════════════════════════════════════════════
    // Legacy: project-level listing + file upload
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Get all projects that have BRD documents.</summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects(CancellationToken ct)
        => Ok(await svc.GetBrdProjectsAsync(ct));

    /// <summary>Get BRD types catalog.</summary>
    [HttpGet("types")]
    public IActionResult GetBrdTypes()
        => Ok(BrdTypeTemplates.ValidTypes.Select(t => new { value = t, label = BrdTypeTemplates.DisplayName(t) }));

    /// <summary>Upload a single file to store raw requirements for a project.</summary>
    [HttpPost("upload/{projectId}")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(string projectId, IFormFile file, [FromQuery] string? templateId = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { error = "File exceeds 10 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = $"File type '{ext}' not supported. Allowed: {string.Join(", ", AllowedExtensions)}" });

        string content;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            content = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { error = "File is empty." });

        var result = await svc.UploadAndGenerateDraftAsync(projectId, file.FileName, content, templateId, ct);
        return Created($"/api/brd/documents?projectId={projectId}", result);
    }

    /// <summary>Upload multiple files to store raw requirements for a project.</summary>
    [HttpPost("upload-batch/{projectId}")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadBatch(string projectId, List<IFormFile> files, [FromQuery] string? templateId = null, CancellationToken ct = default)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        var parsed = new List<(string FileName, string Content)>();
        var errors = new List<string>();

        foreach (var file in files)
        {
            if (file.Length == 0) { errors.Add($"{file.FileName}: empty file"); continue; }
            if (file.Length > MaxFileSize) { errors.Add($"{file.FileName}: exceeds 10 MB limit"); continue; }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) { errors.Add($"{file.FileName}: unsupported type '{ext}'"); continue; }

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(content)) { errors.Add($"{file.FileName}: empty content"); continue; }

            parsed.Add((file.FileName, content));
        }

        if (parsed.Count == 0)
            return BadRequest(new { error = "No valid files to process.", details = errors });

        var result = await svc.UploadBatchAndGenerateDraftAsync(projectId, parsed, templateId, ct);

        if (errors.Count > 0)
            return Ok(new { result, skippedFiles = errors });

        return Created($"/api/brd/documents?projectId={projectId}", result);
    }

    // ── Backward-compat routes that redirect to new structure ──

    /// <summary>Legacy: Get sections by projectId — returns sections from ALL BRD documents.</summary>
    [HttpGet("{projectId}/sections")]
    public async Task<IActionResult> GetSectionsLegacy(string projectId, CancellationToken ct)
    {
        var docs = await svc.GetBrdDocumentsAsync(projectId, ct);
        if (docs.Count == 0) return Ok(Array.Empty<BrdSectionDto>());
        var allSections = new List<BrdSectionDto>();
        foreach (var doc in docs)
            allSections.AddRange(await svc.GetBrdSectionsAsync(doc.Id, ct));
        return Ok(allSections);
    }

    /// <summary>Legacy: Enrich by projectId — enriches ALL BRD documents.</summary>
    [HttpPost("{projectId}/enrich")]
    public async Task<IActionResult> EnrichSectionsLegacy(string projectId, CancellationToken ct)
    {
        var docs = await svc.GetBrdDocumentsAsync(projectId, ct);
        if (docs.Count == 0) return BadRequest(new { error = "No BRD documents found for this project." });
        var results = new List<object>();
        foreach (var doc in docs)
        {
            var result = await svc.EnrichBrdAsync(doc.Id, ct);
            results.Add(new { brdId = doc.Id, brdType = doc.BrdType, result });
        }
        return Ok(new { enriched = results.Count, results });
    }
}
