using GNex.Database;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Entities.Platform.Technology;
using GNex.Core.Interfaces;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace GNex.Services.Platform;

public interface IBrdUploadService
{
    Task<BrdUploadResult> UploadAndGenerateDraftAsync(
        string projectId, string fileName, string fileContent, string? templateId = null, CancellationToken ct = default);

    Task<BrdBatchUploadResult> UploadBatchAndGenerateDraftAsync(
        string projectId, List<(string FileName, string Content)> files, string? templateId = null, CancellationToken ct = default);

    Task<List<BrdSectionDto>> GetBrdSectionsAsync(string projectId, CancellationToken ct = default);

    Task<bool> SaveSectionAsync(string sectionId, string content, CancellationToken ct = default);

    Task<int> ClearBrdSectionsAsync(string projectId, CancellationToken ct = default);

    Task<List<BrdProjectSummaryDto>> ListBrdProjectsAsync(CancellationToken ct = default);
}

public class BrdUploadService(GNexDbContext db, ILlmProvider llm, ILogger<BrdUploadService> logger) : IBrdUploadService
{
    private static readonly Dictionary<string, string[]> SectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["executive_summary"] = ["goal", "objective", "value", "overview", "summary"],
        ["business_objectives"] = ["objective", "kpi", "metric", "target", "outcome"],
        ["stakeholders"] = ["stakeholder", "user", "admin", "doctor", "nurse", "team", "owner"],
        ["scope"] = ["scope", "in scope", "out of scope", "boundary", "feature"],
        ["functional_requirements"] = ["shall", "must", "feature", "workflow", "function", "requirement"],
        ["non_functional_requirements"] = ["performance", "latency", "availability", "security", "scalability", "nfr"],
        ["user_personas"] = ["user", "persona", "patient", "clinician", "doctor", "nurse", "admin", "journey"],
        ["data_requirements"] = ["data", "entity", "table", "field", "schema", "record"],
        ["integration_points"] = ["api", "integration", "fhir", "hl7", "event", "kafka", "external"],
        ["security_requirements"] = ["security", "auth", "authorization", "rbac", "hipaa", "encrypt", "audit"],
        ["compliance"] = ["compliance", "hipaa", "soc2", "gdpr", "policy", "regulatory"],
        ["assumptions"] = ["assumption", "constraint", "dependency", "timeline", "budget"],
        ["dependencies"] = ["dependency", "depends", "blocked", "external", "service"],
        ["acceptance_criteria"] = ["acceptance", "criteria", "done", "test", "verify"],
        ["risks"] = ["risk", "mitigation", "impact", "likelihood", "issue"],
        ["glossary"] = ["term", "definition", "acronym", "glossary"]
    };

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

        // 3. Save raw first so project corpus is complete for LLM generation
        await db.SaveChangesAsync(ct);

        // 4. Regenerate one canonical BRD set for the project (prevents duplicate roots)
        var sectionsCreated = await RebuildBrdSectionsFromTemplateAsync(projectId, template, ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD draft regenerated for project {ProjectId} with {Count} sections from file {File}",
            projectId, sectionsCreated, fileName);

        return new BrdUploadResult(projectId, raw.Id, sectionsCreated, "draft_llm");
    }

    public async Task<BrdBatchUploadResult> UploadBatchAndGenerateDraftAsync(
        string projectId, List<(string FileName, string Content)> files, string? templateId = null, CancellationToken ct = default)
    {
        if (files.Count == 0)
            return new BrdBatchUploadResult(projectId, 0, 0, "empty", []);

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

        // 3. Save raws first so project corpus is complete for LLM generation
        await db.SaveChangesAsync(ct);

        // 4. Rebuild one canonical BRD set for the project from full corpus
        var sectionsCreated = await RebuildBrdSectionsFromTemplateAsync(projectId, template, ct);

        await db.SaveChangesAsync(ct);

        // Update file results with section info
        var status = sectionsCreated > 0 ? "draft_llm" : "raw_only";
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

    public async Task<bool> SaveSectionAsync(string sectionId, string content, CancellationToken ct = default)
    {
        var section = await db.BrdSectionRecords.FirstOrDefaultAsync(s => s.Id == sectionId && s.IsActive, ct);
        if (section is null) return false;

        section.Content = content;
        section.UpdatedAt = DateTimeOffset.UtcNow;
        section.UpdatedBy = "user";
        section.VersionNo++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ClearBrdSectionsAsync(string projectId, CancellationToken ct = default)
    {
        var sections = await db.BrdSectionRecords
            .Where(s => s.BrdId == projectId && s.IsActive)
            .ToListAsync(ct);

        foreach (var s in sections)
        {
            s.IsActive = false;
            s.ArchivedAt = DateTimeOffset.UtcNow;
            s.UpdatedBy = "user";
        }

        await db.SaveChangesAsync(ct);
        return sections.Count;
    }

    public async Task<List<BrdProjectSummaryDto>> ListBrdProjectsAsync(CancellationToken ct = default)
    {
        var results = await db.BrdSectionRecords
            .Where(s => s.IsActive)
            .GroupBy(s => s.BrdId)
            .Select(g => new
            {
                ProjectId = g.Key,
                SectionCount = g.Count(),
                LastUpdated = g.Max(s => s.UpdatedAt)
            })
            .ToListAsync(ct);

        if (results.Count == 0) return [];

        var projectIds = results.Select(r => r.ProjectId).ToList();
        var projects = await db.Projects
            .Where(p => projectIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        return results
            .Select(r =>
            {
                projects.TryGetValue(r.ProjectId, out var proj);
                return new BrdProjectSummaryDto(
                    r.ProjectId,
                    proj?.Name ?? r.ProjectId,
                    proj?.ProjectType ?? "unknown",
                    r.SectionCount,
                    r.LastUpdated,
                    proj?.BrdStatus ?? "draft",
                    proj?.BrdApprovedAt,
                    proj?.BrdApprovedBy);
            })
            .OrderByDescending(r => r.LastUpdated)
            .ToList();
    }

    private sealed record TemplateSectionDefinition(string Type, string Title, int Order, string Prompt);

    private async Task<int> RebuildBrdSectionsFromTemplateAsync(
        string projectId, BrdTemplate template, CancellationToken ct)
    {
        var sections = JsonSerializer.Deserialize<List<TemplateSectionDefinition>>(
            template.SectionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var normalized = sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Type))
            .GroupBy(s => s.Type.Trim().ToLowerInvariant())
            .Select(g => g.OrderBy(x => x.Order).First())
            .OrderBy(s => s.Order)
            .ToList();

        // Delete old records first so every project keeps one canonical BRD set.
        var existingFeedback = await db.BrdFeedbackRecords
            .Where(f => f.BrdId == projectId)
            .ToListAsync(ct);
        if (existingFeedback.Count > 0)
            db.BrdFeedbackRecords.RemoveRange(existingFeedback);

        var existingSections = await db.BrdSectionRecords
            .Where(s => s.BrdId == projectId)
            .ToListAsync(ct);
        if (existingSections.Count > 0)
            db.BrdSectionRecords.RemoveRange(existingSections);

        var sourceItems = await db.RawRequirements
            .Where(r => r.ProjectId == projectId && r.IsActive)
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new { r.SubmittedBy, r.InputText })
            .ToListAsync(ct);

        logger.LogInformation("BRD rebuild: {FileCount} requirement files loaded for project {ProjectId}",
            sourceItems.Count, projectId);

        var sourceSummary = string.Join(", ", sourceItems
            .Select(s => s.SubmittedBy)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct());

        var allTexts = sourceItems.Select(s => s.InputText).ToList();
        var evidenceBySection = BuildSectionEvidenceMap(normalized, allTexts);
        var enforceDiversity = llm.ProviderName.Contains("templatefallback", StringComparison.OrdinalIgnoreCase);

        var sectionRecords = new List<BrdSectionRecord>(normalized.Count);
        var rawContents = new List<string>(normalized.Count);
        foreach (var s in normalized)
        {
            var sectionKey = s.Type.Trim().ToLowerInvariant();
            evidenceBySection.TryGetValue(sectionKey, out var evidenceLines);
            var focusedCorpus = BuildCorpus(evidenceLines ?? [], 12000);
            if (string.IsNullOrWhiteSpace(focusedCorpus) || focusedCorpus == "No uploaded requirement content available.")
                focusedCorpus = BuildFocusedCorpus(s, allTexts, 12000);

            var content = await GenerateSectionWithLlmAsync(projectId, template, s, focusedCorpus, ct);

            var isLow = IsLowSignalFallback(content);
            var isDup = enforceDiversity && IsDuplicateContent(content, rawContents);
            if (isLow || isDup)
            {
                logger.LogWarning("BRD section [{Section}] rejected: isLow={IsLow} isDup={IsDup} provider={Provider} contentStart={Start}",
                    s.Type, isLow, isDup, llm.ProviderName, content[..Math.Min(80, content.Length)]);
                content = BuildDeterministicSectionDraft(s, focusedCorpus);
            }
            else
            {
                logger.LogInformation("BRD section [{Section}] accepted from provider={Provider}, len={Len}",
                    s.Type, llm.ProviderName, content.Length);
            }

            rawContents.Add(content);
            sectionRecords.Add(new BrdSectionRecord
            {
                BrdId = projectId,
                SectionType = s.Type.Trim().ToLowerInvariant(),
                Order = s.Order,
                Content = content + $"\n\n---\nSources: {(string.IsNullOrWhiteSpace(sourceSummary) ? "n/a" : sourceSummary)}",
                DiagramsJson = "[]"
            });
        }

        db.BrdSectionRecords.AddRange(sectionRecords);
        return sectionRecords.Count;
    }

    private async Task<string> GenerateSectionWithLlmAsync(
        string projectId,
        BrdTemplate template,
        TemplateSectionDefinition section,
        string corpus,
        CancellationToken ct)
    {
        var fallback = $"# {section.Title}\n\n{section.Prompt}\n\nGenerated without LLM enrichment.";
        if (!llm.IsAvailable)
            return fallback;

        try
        {
            var response = await llm.GenerateAsync(new LlmPrompt
            {
                RequestingAgent = "BrdUploadService",
                Temperature = 0.3,
                MaxTokens = 3000,
                SystemPrompt = """You are a senior BRD (Business Requirements Document) analyst with deep domain expertise. You produce comprehensive, implementation-ready BRD sections in markdown. Each section must be thorough, well-structured, and grounded entirely in the provided requirements corpus. Extract maximum detail from the source material — do not summarise when detail is available.""",
                UserPrompt = $"""
You are writing the "{section.Title}" section of a BRD document.

SectionType: {section.Type}
SectionTitle: {section.Title}
Template: {template.Name} ({template.ProjectType})

Prompt:
{section.Prompt}

The requirements corpus below has been compiled from multiple uploaded requirement files. Analyse ALL of it thoroughly and produce a detailed, comprehensive section that addresses EVERY relevant point from the corpus.

Requirements corpus:
{corpus}

Output rules:
- Write in markdown. Start with a level-2 heading (## {section.Title}).
- Be DETAILED and COMPREHENSIVE — extract every relevant requirement, constraint, and specification from the corpus.
- Group related items under level-3 sub-headings (### Sub-topic).
- Use bullet lists for individual requirements; include specifics (numbers, names, standards, protocols).
- Where the corpus contains measurable targets, thresholds, or SLAs, include them verbatim.
- Cross-reference related requirements from different source files when they address the same topic.
- Do NOT invent facts. Every statement must trace back to the corpus.
- Do NOT repeat content that belongs in other BRD sections — stay focused on this section's scope.
- Aim for thoroughness: a reader should not need to go back to the source files for this section's topic.
"""
            }, ct);

            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
                return response.Content.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM generation failed for BRD section {SectionType} in project {ProjectId}", section.Type, projectId);
        }

        return fallback;
    }

    private static string BuildCorpus(List<string> inputTexts, int maxChars)
    {
        if (inputTexts.Count == 0)
            return "No uploaded requirement content available.";

        var sb = new StringBuilder();
        foreach (var text in inputTexts)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();
            if (sb.Length + trimmed.Length + 2 > maxChars)
            {
                var remaining = maxChars - sb.Length;
                if (remaining > 0)
                    sb.Append(trimmed[..Math.Min(trimmed.Length, remaining)]);
                break;
            }

            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(trimmed);
        }

        return sb.Length == 0 ? "No uploaded requirement content available." : sb.ToString();
    }

    private static string BuildFocusedCorpus(TemplateSectionDefinition section, List<string> inputTexts, int maxChars)
    {
        var keywords = SectionKeywords.TryGetValue(section.Type.Trim().ToLowerInvariant(), out var words)
            ? words
            : [section.Title, section.Type, "requirement", "must", "should"];

        var ranked = new List<(string Line, int Score)>();
        foreach (var raw in inputTexts)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.Length < 8)
                    continue;

                var score = 0;
                foreach (var k in keywords)
                    if (!string.IsNullOrWhiteSpace(k) && line.Contains(k, StringComparison.OrdinalIgnoreCase))
                        score++;

                if (line.Contains("must", StringComparison.OrdinalIgnoreCase) || line.Contains("shall", StringComparison.OrdinalIgnoreCase))
                    score++;

                if (score > 0)
                    ranked.Add((line, score));
            }
        }

        if (ranked.Count == 0)
            return BuildCorpus(inputTexts, maxChars);

        var selected = ranked
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Line.Length)
            .Select(x => x.Line)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        return BuildCorpus(selected, maxChars);
    }

    private static Dictionary<string, List<string>> BuildSectionEvidenceMap(
        List<TemplateSectionDefinition> sections,
        List<string> inputTexts)
    {
        var map = sections.ToDictionary(
            s => s.Type.Trim().ToLowerInvariant(),
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        var allLines = ExtractEvidenceLines(inputTexts);
        foreach (var line in allLines)
        {
            string? bestSection = null;
            var bestScore = 0;
            foreach (var section in sections)
            {
                var sectionKey = section.Type.Trim().ToLowerInvariant();
                var score = ScoreLineForSection(line, section, sectionKey);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSection = sectionKey;
                }
            }

            if (bestSection is not null && bestScore > 0)
                map[bestSection].Add(line);
        }

        // Ensure each section gets substantial evidence from across all files.
        var globalTop = allLines.Take(80).ToList();
        foreach (var section in sections)
        {
            var key = section.Type.Trim().ToLowerInvariant();
            var current = map[key]
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(80)
                .ToList();

            if (current.Count < 15)
            {
                var fallback = globalTop
                    .Where(l => !current.Contains(l, StringComparer.OrdinalIgnoreCase))
                    .Take(15 - current.Count)
                    .ToList();
                current.AddRange(fallback);
            }

            map[key] = current;
        }

        return map;
    }

    private static List<string> ExtractEvidenceLines(List<string> inputTexts)
    {
        return inputTexts
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .SelectMany(t => t.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(l => l.Length >= 10)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreLineForSection(string line, TemplateSectionDefinition section, string sectionKey)
    {
        var score = 0;
        if (line.Contains(section.Title, StringComparison.OrdinalIgnoreCase))
            score += 2;

        if (!string.IsNullOrWhiteSpace(section.Prompt) && line.Contains(section.Prompt, StringComparison.OrdinalIgnoreCase))
            score += 2;

        if (SectionKeywords.TryGetValue(sectionKey, out var words))
        {
            foreach (var w in words)
                if (!string.IsNullOrWhiteSpace(w) && line.Contains(w, StringComparison.OrdinalIgnoreCase))
                    score++;
        }

        if (line.Contains("must", StringComparison.OrdinalIgnoreCase) || line.Contains("shall", StringComparison.OrdinalIgnoreCase))
            score++;

        return score;
    }

    private static bool IsLowSignalFallback(string content)
        => content.Contains("Template Fallback", StringComparison.OrdinalIgnoreCase)
           || content.Contains("Generated without LLM enrichment", StringComparison.OrdinalIgnoreCase)
           || content.Contains("Configure Llm:ApiKey", StringComparison.OrdinalIgnoreCase);

    private static bool IsDuplicateContent(string candidate, IEnumerable<string> existing)
    {
        var normalizedCandidate = Normalize(candidate);
        if (normalizedCandidate.Length == 0)
            return false;

        return existing.Any(e => Normalize(e) == normalizedCandidate);
    }

    private static string Normalize(string value)
        => new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    private static string BuildDeterministicSectionDraft(TemplateSectionDefinition section, string focusedCorpus)
    {
        var lines = focusedCorpus
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(l => $"- {l}")
            .ToList();

        if (lines.Count == 0)
            lines.Add("- No direct requirement lines matched this section; review uploaded files for more detail.");

        return $"## {section.Title}\n\n*{section.Prompt}*\n\n### Key Requirements from Source Files\n\n{string.Join("\n", lines)}";
    }
}
