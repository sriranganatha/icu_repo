using GNex.Database;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Entities.Platform.Technology;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GNex.Services.Platform;

public interface IBrdUploadService
{
    Task<BrdUploadResult> UploadAndGenerateDraftAsync(
        string projectId, string fileName, string fileContent, string? templateId = null, CancellationToken ct = default);

    Task<BrdBatchUploadResult> UploadBatchAndGenerateDraftAsync(
        string projectId, List<(string FileName, string Content)> files, string? templateId = null, CancellationToken ct = default);

    Task<List<BrdSectionDto>> GetBrdSectionsAsync(string projectId, CancellationToken ct = default);
}

public class BrdUploadService(GNexDbContext db, ILogger<BrdUploadService> logger) : IBrdUploadService
{
    public async Task<BrdUploadResult> UploadAndGenerateDraftAsync(
        string projectId, string fileName, string fileContent, string? templateId = null, CancellationToken ct = default)
    {
        // 1. Store as RawRequirement
        var raw = new RawRequirement
        {
            ProjectId = projectId,
            InputText = fileContent,
            InputType = "file",
            SubmittedBy = "upload:" + fileName,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        db.RawRequirements.Add(raw);

        // 2. Find BRD template (use specified or default)
        var template = templateId != null
            ? await db.BrdTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct)
            : await db.BrdTemplates.FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, ct);

        if (template == null)
        {
            logger.LogWarning("No BRD template found, creating raw requirement only");
            await db.SaveChangesAsync(ct);
            return new BrdUploadResult(projectId, raw.Id, 0, "raw_only");
        }

        // 3. Check if BRD sections already exist for this project — don't duplicate
        var existingSections = await db.BrdSectionRecords
            .AnyAsync(s => s.BrdId == projectId && s.IsActive, ct);

        if (existingSections)
        {
            // Sections already generated — just store the raw requirement
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Raw requirement stored for project {ProjectId} from file {File} (BRD sections already exist)",
                projectId, fileName);
            return new BrdUploadResult(projectId, raw.Id, 0, "appended");
        }

        // 4. Parse template sections and create draft BrdSectionRecords
        var sectionsCreated = await CreateBrdSectionsFromTemplate(projectId, template, [fileName], ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD draft created for project {ProjectId} with {Count} sections from file {File}",
            projectId, sectionsCreated, fileName);

        return new BrdUploadResult(projectId, raw.Id, sectionsCreated, "draft");
    }

    public async Task<BrdBatchUploadResult> UploadBatchAndGenerateDraftAsync(
        string projectId, List<(string FileName, string Content)> files, string? templateId = null, CancellationToken ct = default)
    {
        // 1. Store all files as RawRequirements
        var fileResults = new List<BrdFileResult>();
        foreach (var (fileName, content) in files)
        {
            var raw = new RawRequirement
            {
                ProjectId = projectId,
                InputText = content,
                InputType = "file",
                SubmittedBy = "upload:" + fileName,
                SubmittedAt = DateTimeOffset.UtcNow
            };
            db.RawRequirements.Add(raw);
            fileResults.Add(new BrdFileResult(fileName, raw.Id, 0, "stored"));
        }

        // 2. Find BRD template
        var template = templateId != null
            ? await db.BrdTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct)
            : await db.BrdTemplates.FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, ct);

        if (template == null)
        {
            logger.LogWarning("No BRD template found, creating raw requirements only");
            await db.SaveChangesAsync(ct);
            return new BrdBatchUploadResult(projectId, files.Count, 0, "raw_only", fileResults);
        }

        // 3. Remove any existing BRD sections for this project (regenerate from all sources)
        var existing = await db.BrdSectionRecords
            .Where(s => s.BrdId == projectId)
            .ToListAsync(ct);
        if (existing.Count > 0)
            db.BrdSectionRecords.RemoveRange(existing);

        // 4. Create ONE set of BRD sections referencing ALL uploaded files
        var allFileNames = files.Select(f => f.FileName).ToList();
        var sectionsCreated = await CreateBrdSectionsFromTemplate(projectId, template, allFileNames, ct);

        await db.SaveChangesAsync(ct);

        // Update file results with section info
        var status = sectionsCreated > 0 ? "draft" : "raw_only";
        fileResults = fileResults.Select(f => f with { Status = status }).ToList();

        logger.LogInformation("BRD batch: {FileCount} files stored, {Sections} sections created for project {ProjectId}",
            files.Count, sectionsCreated, projectId);

        return new BrdBatchUploadResult(projectId, files.Count, sectionsCreated, status, fileResults);
    }

    public async Task<List<BrdSectionDto>> GetBrdSectionsAsync(string projectId, CancellationToken ct = default)
    {
        return await db.BrdSectionRecords
            .Where(s => s.BrdId == projectId && s.IsActive)
            .OrderBy(s => s.Order)
            .Select(s => new BrdSectionDto(s.Id, s.SectionType, s.Order, s.Content, s.DiagramsJson))
            .ToListAsync(ct);
    }

    private sealed record TemplateSectionDefinition(string Type, string Title, int Order, string Prompt);

    private Task<int> CreateBrdSectionsFromTemplate(
        string projectId, BrdTemplate template, List<string> sourceFileNames, CancellationToken ct)
    {
        var sections = JsonSerializer.Deserialize<List<TemplateSectionDefinition>>(
            template.SectionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var sourceList = string.Join(", ", sourceFileNames);

        var sectionRecords = sections.Select(s => new BrdSectionRecord
        {
            BrdId = projectId,
            SectionType = s.Type,
            Order = s.Order,
            Content = $"[DRAFT] {s.Title}\n\n{s.Prompt}\n\n---\nSources ({sourceFileNames.Count} file(s)): {sourceList}\nExtracted content pending AI enrichment.",
            DiagramsJson = "[]"
        }).ToList();

        db.BrdSectionRecords.AddRange(sectionRecords);
        return Task.FromResult(sectionRecords.Count);
    }
}
