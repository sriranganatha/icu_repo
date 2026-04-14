using GNex.Core.Interfaces;
using GNex.Database;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Entities.Platform.Technology;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace GNex.Services.Platform;

public interface IBrdUploadService
{
    // ── Legacy single/batch upload (kept for backward compat) ──
    Task<BrdUploadResult> UploadAndGenerateDraftAsync(
        string projectId, string fileName, string fileContent, string? templateId = null, CancellationToken ct = default);

    Task<BrdBatchUploadResult> UploadBatchAndGenerateDraftAsync(
        string projectId, List<(string FileName, string Content)> files, string? templateId = null, CancellationToken ct = default);

    // ── Multi-BRD operations ──
    Task<BrdDocumentCreateResult> CreateBrdDocumentsAsync(CreateBrdDocumentRequest request, CancellationToken ct = default);
    Task<List<BrdDocumentDto>> GetBrdDocumentsAsync(string? projectId = null, CancellationToken ct = default);
    Task<BrdDocumentDto?> GetBrdDocumentAsync(string brdId, CancellationToken ct = default);
    Task<bool> UpdateBrdDocumentAsync(string brdId, UpdateBrdDocumentRequest request, CancellationToken ct = default);
    Task<bool> DeleteBrdDocumentAsync(string brdId, CancellationToken ct = default);
    Task<List<BrdDocumentDto>> GetGroupSiblingsAsync(string brdId, CancellationToken ct = default);

    // ── Section operations ──
    Task<List<BrdSectionDto>> GetBrdSectionsAsync(string brdId, CancellationToken ct = default);
    Task<bool> UpdateSectionAsync(string sectionId, string content, CancellationToken ct = default);
    Task<int> DeleteSectionsAsync(string brdId, CancellationToken ct = default);

    // ── Project listing (backward compat) ──
    Task<List<BrdProjectDto>> GetBrdProjectsAsync(CancellationToken ct = default);

    // ── AI Enrichment ──
    Task<BrdEnrichResult> EnrichBrdAsync(string brdId, CancellationToken ct = default);
}

public class BrdUploadService(GNexDbContext db, ILlmProvider llm, ILogger<BrdUploadService> logger) : IBrdUploadService
{
    // ═══════════════════════════════════════════════════════════════
    // Multi-BRD: Create one BrdDocument per selected type
    // ═══════════════════════════════════════════════════════════════
    public async Task<BrdDocumentCreateResult> CreateBrdDocumentsAsync(CreateBrdDocumentRequest request, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.IsActive, ct)
            ?? throw new InvalidOperationException($"Project {request.ProjectId} not found.");

        var docs = new List<BrdDocument>();

        // When multiple types are selected, assign a shared GroupId so all
        // sibling BRDs reference the same set of requirement source documents.
        var groupId = request.BrdTypes.Count > 1 ? Guid.NewGuid().ToString("N") : null;

        foreach (var brdType in request.BrdTypes)
        {
            if (!BrdTypeTemplates.IsValid(brdType))
                throw new ArgumentException($"Invalid BRD type: {brdType}");

            var typeDisplay = BrdTypeTemplates.DisplayName(brdType);
            var title = request.BrdTypes.Count == 1
                ? request.Title
                : $"{request.Title} — {typeDisplay}";

            var doc = new BrdDocument
            {
                ProjectId = request.ProjectId,
                Title = title,
                Description = request.Description ?? string.Empty,
                BrdType = brdType,
                Instructions = request.Instructions ?? string.Empty,
                GroupId = groupId,
                Status = "draft"
            };
            db.BrdDocuments.Add(doc);
            docs.Add(doc);

            // Create sections from the type template
            var sectionDefs = BrdTypeTemplates.GetSections(brdType);
            foreach (var s in sectionDefs)
            {
                db.BrdSectionRecords.Add(new BrdSectionRecord
                {
                    BrdId = doc.Id,
                    SectionType = s.Type,
                    Order = s.Order,
                    Content = $"[DRAFT] {s.Title}\n\n{s.Prompt}\n\n---\nPending AI enrichment.",
                    DiagramsJson = "[]"
                });
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created {Count} BRD document(s) for project {ProjectId}: {Types}{Group}",
            docs.Count, request.ProjectId, string.Join(", ", request.BrdTypes),
            groupId is not null ? $" [group={groupId[..8]}]" : "");

        var dtos = docs.Select(d => MapToDto(d, project.Name)).ToList();
        return new BrdDocumentCreateResult(request.ProjectId, docs.Count, groupId, dtos);
    }

    // ═══════════════════════════════════════════════════════════════
    // BRD Document CRUD
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<BrdDocumentDto>> GetBrdDocumentsAsync(string? projectId = null, CancellationToken ct = default)
    {
        var query = db.BrdDocuments
            .Include(d => d.Project)
            .Where(d => d.IsActive);

        if (!string.IsNullOrEmpty(projectId))
            query = query.Where(d => d.ProjectId == projectId);

        var docs = await query
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        var result = new List<BrdDocumentDto>();
        foreach (var d in docs)
        {
            var sectionCount = await db.BrdSectionRecords.CountAsync(s => s.BrdId == d.Id && s.IsActive, ct);
            result.Add(new BrdDocumentDto(
                d.Id, d.ProjectId, d.Project?.Name ?? d.ProjectId,
                d.Title, d.Description, d.BrdType, BrdTypeTemplates.DisplayName(d.BrdType),
                d.Instructions, d.Status, sectionCount,
                d.CreatedAt, d.UpdatedAt, d.ApprovedAt, d.ApprovedBy, d.GroupId));
        }
        return result;
    }

    public async Task<BrdDocumentDto?> GetBrdDocumentAsync(string brdId, CancellationToken ct = default)
    {
        var d = await db.BrdDocuments.Include(x => x.Project)
            .FirstOrDefaultAsync(x => x.Id == brdId && x.IsActive, ct);
        if (d is null) return null;

        var sectionCount = await db.BrdSectionRecords.CountAsync(s => s.BrdId == d.Id && s.IsActive, ct);
        return new BrdDocumentDto(
            d.Id, d.ProjectId, d.Project?.Name ?? d.ProjectId,
            d.Title, d.Description, d.BrdType, BrdTypeTemplates.DisplayName(d.BrdType),
            d.Instructions, d.Status, sectionCount,
            d.CreatedAt, d.UpdatedAt, d.ApprovedAt, d.ApprovedBy, d.GroupId);
    }

    public async Task<bool> UpdateBrdDocumentAsync(string brdId, UpdateBrdDocumentRequest request, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null) return false;

        if (request.Title is not null) doc.Title = request.Title;
        if (request.Description is not null) doc.Description = request.Description;
        if (request.Instructions is not null) doc.Instructions = request.Instructions;
        if (request.ProjectId is not null) doc.ProjectId = request.ProjectId;
        doc.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteBrdDocumentAsync(string brdId, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null) return false;

        // Soft-delete sections
        var sections = await db.BrdSectionRecords.Where(s => s.BrdId == brdId).ToListAsync(ct);
        foreach (var s in sections) s.IsActive = false;

        doc.IsActive = false;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Deleted BRD document {BrdId} with {Sections} sections", brdId, sections.Count);
        return true;
    }

    public async Task<List<BrdDocumentDto>> GetGroupSiblingsAsync(string brdId, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null || string.IsNullOrEmpty(doc.GroupId))
            return [];

        return await db.BrdDocuments
            .Include(d => d.Project)
            .Include(d => d.Sections)
            .Where(d => d.GroupId == doc.GroupId && d.IsActive)
            .OrderBy(d => d.CreatedAt)
            .Select(d => MapToDto(d, d.Project!.Name))
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Section operations
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<BrdSectionDto>> GetBrdSectionsAsync(string brdId, CancellationToken ct = default)
    {
        return await db.BrdSectionRecords
            .Where(s => s.BrdId == brdId && s.IsActive)
            .OrderBy(s => s.Order)
            .Select(s => new BrdSectionDto(s.Id, s.SectionType, s.Order, s.Content, s.DiagramsJson))
            .ToListAsync(ct);
    }

    public async Task<bool> UpdateSectionAsync(string sectionId, string content, CancellationToken ct = default)
    {
        var section = await db.BrdSectionRecords.FirstOrDefaultAsync(s => s.Id == sectionId && s.IsActive, ct);
        if (section is null) return false;

        section.Content = content;
        section.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> DeleteSectionsAsync(string brdId, CancellationToken ct = default)
    {
        var sections = await db.BrdSectionRecords.Where(s => s.BrdId == brdId && s.IsActive).ToListAsync(ct);
        foreach (var s in sections) s.IsActive = false;
        await db.SaveChangesAsync(ct);
        return sections.Count;
    }

    // ═══════════════════════════════════════════════════════════════
    // AI Enrichment (per BRD document)
    // ═══════════════════════════════════════════════════════════════
    public async Task<BrdEnrichResult> EnrichBrdAsync(string brdId, CancellationToken ct = default)
    {
        var brdDoc = await db.BrdDocuments.Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (brdDoc is null)
            return new BrdEnrichResult(brdId, 0, 0, "not_found");

        var projectId = brdDoc.ProjectId;

        // 1. Load raw requirements as source material
        var rawDocs = await db.RawRequirements
            .Where(r => r.ProjectId == projectId && r.IsActive)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => new { r.SubmittedBy, r.InputText })
            .ToListAsync(ct);

        if (rawDocs.Count == 0)
        {
            logger.LogWarning("No raw requirements found for project {ProjectId}, cannot enrich BRD {BrdId}", projectId, brdId);
            return new BrdEnrichResult(brdId, 0, 0, "no_sources");
        }

        // 2. Build source-material block
        const int maxSourceChars = 120_000;
        var sourceBuilder = new StringBuilder();
        foreach (var doc in rawDocs)
        {
            var header = $"### Source: {doc.SubmittedBy}\n";
            if (sourceBuilder.Length + header.Length + doc.InputText.Length > maxSourceChars)
            {
                sourceBuilder.AppendLine("... (additional sources truncated for context limit)");
                break;
            }
            sourceBuilder.AppendLine(header);
            sourceBuilder.AppendLine(doc.InputText);
            sourceBuilder.AppendLine();
        }
        var sourceBlock = sourceBuilder.ToString();

        var projectName = brdDoc.Project?.Name ?? projectId;
        var projectType = brdDoc.Project?.ProjectType ?? "unknown";
        var brdTypeDisplay = BrdTypeTemplates.DisplayName(brdDoc.BrdType);

        // 3. Load draft sections
        var sections = await db.BrdSectionRecords
            .Where(s => s.BrdId == brdId && s.IsActive)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        if (sections.Count == 0)
            return new BrdEnrichResult(brdId, 0, 0, "no_sections");

        int enriched = 0, failed = 0;

        // 4. Build custom instructions context
        var instructionsBlock = string.IsNullOrWhiteSpace(brdDoc.Instructions)
            ? ""
            : $"\n\nCustom Instructions from user:\n{brdDoc.Instructions}";

        // 5. Enrich each section via LLM
        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var sectionTitle = FormatSectionTitle(section.SectionType);
                var prompt = new LlmPrompt
                {
                    SystemPrompt = $"""
                        You are a senior business analyst writing a Business Requirements Document (BRD).
                        Project: {projectName} (type: {projectType}).
                        BRD Type: {brdTypeDisplay}.
                        BRD Title: {brdDoc.Title}.
                        Write the "{sectionTitle}" section of the BRD based on the provided source documents.
                        {instructionsBlock}

                        Guidelines:
                        - Be comprehensive, structured, and professional.
                        - Use markdown formatting with headers, bullet points, and tables where appropriate.
                        - Reference specific details from the source material.
                        - If the source material doesn't cover this section well, note what additional information is needed.
                        - Do NOT include "[DRAFT]" prefix — this will be production content.
                        - Write ONLY the section content, no preamble.
                        """,
                    UserPrompt = $"""
                        ## Section to Write: {sectionTitle}

                        Section type: {section.SectionType}
                        Current placeholder content: {section.Content}

                        ## Source Documents

                        {sourceBlock}

                        Write the complete "{sectionTitle}" section now.
                        """,
                    Temperature = 0.3,
                    MaxTokens = 4096,
                    RequestingAgent = "BrdEnrichment"
                };

                var response = await llm.GenerateAsync(prompt, ct);
                if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
                {
                    section.Content = response.Content;
                    section.UpdatedAt = DateTimeOffset.UtcNow;
                    enriched++;
                    logger.LogInformation("Enriched BRD section {Section} for BRD {BrdId}", section.SectionType, brdId);
                }
                else
                {
                    failed++;
                    logger.LogWarning("LLM enrichment failed for section {Section}: {Content}", section.SectionType, response.Content);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Error enriching BRD section {Section} for BRD {BrdId}", section.SectionType, brdId);
            }
        }

        // 6. Update BRD document status
        if (enriched > 0 && failed == 0)
        {
            brdDoc.Status = "enriched";
            brdDoc.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        var status = failed == 0 ? "enriched" : (enriched > 0 ? "partial" : "failed");
        logger.LogInformation("BRD enrichment complete for {BrdId}: {Enriched} enriched, {Failed} failed",
            brdId, enriched, failed);

        return new BrdEnrichResult(brdId, enriched, failed, status);
    }

    // ═══════════════════════════════════════════════════════════════
    // Legacy: project-level listing (backward compat for Index page)
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<BrdProjectDto>> GetBrdProjectsAsync(CancellationToken ct = default)
    {
        return await db.Projects
            .Where(p => p.IsActive && p.BrdDocuments.Any(d => d.IsActive))
            .Select(p => new BrdProjectDto(
                p.Id,
                p.Name,
                p.ProjectType,
                p.BrdDocuments.Count(d => d.IsActive),
                p.BrdDocuments.Where(d => d.IsActive).Max(d => d.UpdatedAt),
                // Derive aggregate status: approved > in_review > enriched > draft > rejected
                p.BrdDocuments.Any(d => d.IsActive && d.Status == "approved") ? "approved"
                : p.BrdDocuments.Any(d => d.IsActive && d.Status == "in_review") ? "in_review"
                : p.BrdDocuments.Any(d => d.IsActive && d.Status == "enriched") ? "enriched"
                : p.BrdDocuments.Any(d => d.IsActive && d.Status == "draft") ? "draft"
                : "rejected"))
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Legacy: single/batch upload → creates a "general" BrdDocument
    // ═══════════════════════════════════════════════════════════════
    public async Task<BrdUploadResult> UploadAndGenerateDraftAsync(
        string projectId, string fileName, string fileContent, string? templateId = null, CancellationToken ct = default)
    {
        // Store raw requirement
        var raw = new RawRequirement
        {
            ProjectId = projectId,
            InputText = fileContent,
            InputType = "file",
            SubmittedBy = "upload:" + fileName,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        db.RawRequirements.Add(raw);
        await db.SaveChangesAsync(ct);

        return new BrdUploadResult(projectId, raw.Id, 0, "stored");
    }

    public async Task<BrdBatchUploadResult> UploadBatchAndGenerateDraftAsync(
        string projectId, List<(string FileName, string Content)> files, string? templateId = null, CancellationToken ct = default)
    {
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
        await db.SaveChangesAsync(ct);

        return new BrdBatchUploadResult(projectId, files.Count, 0, "stored", fileResults);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════
    private static string FormatSectionTitle(string sectionType) =>
        string.Join(' ', sectionType.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    private static BrdDocumentDto MapToDto(BrdDocument d, string projectName)
    {
        var sectionCount = d.Sections?.Count(s => s.IsActive) ?? 0;
        return new BrdDocumentDto(
            d.Id, d.ProjectId, projectName, d.Title, d.Description,
            d.BrdType, BrdTypeTemplates.DisplayName(d.BrdType),
            d.Instructions, d.Status, sectionCount,
            d.CreatedAt, d.UpdatedAt, d.ApprovedAt, d.ApprovedBy, d.GroupId);
    }
}
