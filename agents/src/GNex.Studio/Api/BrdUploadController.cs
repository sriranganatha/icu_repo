using GNex.Services.Platform;
using GNex.Services.Dtos.Platform;
using Microsoft.AspNetCore.Mvc;

namespace GNex.Studio.Api;

[ApiController]
[Route("api/brd")]
public class BrdUploadController(IBrdUploadService svc, IBrdWorkflowService workflow) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = [".txt", ".md", ".csv", ".json", ".pdf", ".docx"];
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    /// <summary>Upload a single file to generate a draft BRD for a project.</summary>
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

        // Read file content as text
        string content;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            content = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { error = "File is empty." });

        var result = await svc.UploadAndGenerateDraftAsync(projectId, file.FileName, content, templateId, ct);
        return Created($"/api/brd/{projectId}/sections", result);
    }

    /// <summary>Upload multiple files to generate a draft BRD for a project.</summary>
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

        return Created($"/api/brd/{projectId}/sections", result);
    }

    /// <summary>List all projects that have BRD sections.</summary>
    [HttpGet("projects")]
    public async Task<IActionResult> ListBrdProjects(CancellationToken ct)
        => Ok(await svc.ListBrdProjectsAsync(ct));

    /// <summary>Get BRD draft sections for a project.</summary>
    [HttpGet("{projectId}/sections")]
    public async Task<IActionResult> GetSections(string projectId, CancellationToken ct)
        => Ok(await svc.GetBrdSectionsAsync(projectId, ct));

    /// <summary>Save (update) a single BRD section's content.</summary>
    [HttpPut("sections/{sectionId}")]
    public async Task<IActionResult> SaveSection(string sectionId, [FromBody] SaveSectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Content))
            return BadRequest(new { error = "Content is required." });

        var updated = await svc.SaveSectionAsync(sectionId, request.Content, ct);
        return updated ? Ok(new { message = "Section saved." }) : NotFound(new { error = "Section not found." });
    }

    /// <summary>Clear (soft-delete) all BRD sections for a project.</summary>
    [HttpDelete("{projectId}/sections")]
    public async Task<IActionResult> ClearSections(string projectId, CancellationToken ct)
    {
        var count = await svc.ClearBrdSectionsAsync(projectId, ct);
        return Ok(new { message = $"{count} section(s) cleared.", count });
    }

    // ── BRD Workflow endpoints ────────────────────────────────

    /// <summary>Get BRD workflow status for a project.</summary>
    [HttpGet("{projectId}/status")]
    public async Task<IActionResult> GetBrdStatus(string projectId, CancellationToken ct)
    {
        var result = await workflow.GetStatusAsync(projectId, ct);
        return result.Success ? Ok(result) : NotFound(new { error = result.Message });
    }

    /// <summary>Submit a BRD for review (Draft → InReview).</summary>
    [HttpPost("{projectId}/submit")]
    public async Task<IActionResult> SubmitForReview(string projectId, [FromBody] BrdWorkflowRequest request, CancellationToken ct)
    {
        var reviewer = request?.Reviewer ?? "user";
        var result = await workflow.SubmitForReviewAsync(projectId, reviewer, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message, status = result.Status });
    }

    /// <summary>Approve a BRD and trigger the agent pipeline (InReview → Approved).</summary>
    [HttpPost("{projectId}/approve")]
    public async Task<IActionResult> Approve(string projectId, [FromBody] BrdApprovalRequest request, CancellationToken ct)
    {
        var reviewer = request?.Reviewer ?? "user";
        var result = await workflow.ApproveAsync(projectId, reviewer, request?.Comment, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message, status = result.Status });
    }

    /// <summary>Reject a BRD (InReview → Rejected).</summary>
    [HttpPost("{projectId}/reject")]
    public async Task<IActionResult> Reject(string projectId, [FromBody] BrdRejectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
            return BadRequest(new { error = "Reason is required." });

        var reviewer = request.Reviewer ?? "user";
        var result = await workflow.RejectAsync(projectId, reviewer, request.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message, status = result.Status });
    }

    /// <summary>Request changes on a BRD (InReview → Draft).</summary>
    [HttpPost("{projectId}/request-changes")]
    public async Task<IActionResult> RequestChanges(string projectId, [FromBody] BrdRequestChangesRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Feedback))
            return BadRequest(new { error = "Feedback is required." });

        var reviewer = request.Reviewer ?? "user";
        var result = await workflow.RequestChangesAsync(projectId, reviewer, request.Feedback, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Message, status = result.Status });
    }
}

public sealed record SaveSectionRequest(string Content);
public sealed record BrdWorkflowRequest(string? Reviewer);
public sealed record BrdApprovalRequest(string? Reviewer, string? Comment);
public sealed record BrdRejectRequest(string? Reviewer, string Reason);
public sealed record BrdRequestChangesRequest(string? Reviewer, string Feedback);
