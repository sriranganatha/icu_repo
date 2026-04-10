using GNex.Services.Platform;
using GNex.Services.Dtos.Platform;
using Microsoft.AspNetCore.Mvc;

namespace GNex.Studio.Api;

[ApiController]
[Route("api/brd")]
public class BrdUploadController(IBrdUploadService svc) : ControllerBase
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

    /// <summary>Get BRD draft sections for a project.</summary>
    [HttpGet("{projectId}/sections")]
    public async Task<IActionResult> GetSections(string projectId, CancellationToken ct)
        => Ok(await svc.GetBrdSectionsAsync(projectId, ct));
}
