using System.Text.RegularExpressions;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Requirements;

public sealed partial class RequirementParser : IRequirementsReader
{
    private readonly ILogger<RequirementParser> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly string[] s_docExtensions = [".md", ".yaml", ".yml"];
    private static readonly string[] s_requirementSignals =
    [
        "shall", "must", "should", "will", "given", "when", "then",
        "as a", "i want", "system", "user", "enable", "allow",
        "capture", "validate", "create", "update", "delete", "generate", "process"
    ];

    private static readonly string[] s_structuralHeadings =
    [
        "table of contents", "toc", "purpose", "scope", "references", "definitions",
        "glossary", "revision history", "approvals", "owners", "document control",
        "background", "introduction"
    ];

    private int _idCounter;

    public RequirementParser(ILogger<RequirementParser> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<List<Requirement>> ReadFromBrdAsync(string projectId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GNex.Database.GNexDbContext>();

        // Multi-BRD: find all BrdDocument IDs for this project, then pull sections from ALL of them
        var brdDocIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(
                db.BrdDocuments
                    .Where(d => d.ProjectId == projectId && d.IsActive)
                    .OrderBy(d => d.CreatedAt)
                    .Select(d => new { d.Id, d.BrdType, d.Title }),
                ct);

        if (brdDocIds.Count == 0)
        {
            _logger.LogWarning("No BRD documents found for project {ProjectId}", projectId);
            return [];
        }

        var docIdList = brdDocIds.Select(d => d.Id).ToList();

        var sections = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(
                db.BrdSectionRecords
                    .Where(s => docIdList.Contains(s.BrdId) && s.IsActive)
                    .OrderBy(s => s.BrdId).ThenBy(s => s.Order),
                ct);

        if (sections.Count == 0)
        {
            _logger.LogWarning("No BRD sections found across {DocCount} documents for project {ProjectId}",
                brdDocIds.Count, projectId);
            return [];
        }

        // Build a lookup of BrdDocumentId → BrdType for richer module tagging
        var docTypeMap = brdDocIds.ToDictionary(d => d.Id, d => d.BrdType);

        var requirements = new List<Requirement>();
        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            var lines = section.Content.Split('\n');
            var brdType = docTypeMap.GetValueOrDefault(section.BrdId, "general");
            var moduleSuffix = brdType != "general" ? $" ({brdType.Replace('_', ' ')})" : "";
            var parsed = ParseLines(lines, section.SectionType, $"BRD{moduleSuffix}");
            requirements.AddRange(parsed);
        }

        // Stamp ProjectId on all parsed requirements so downstream agents and DB persistence can scope them
        foreach (var req in requirements)
            req.ProjectId = projectId;

        _logger.LogInformation("Parsed {Count} requirements from {DocCount} BRD documents for project {ProjectId}",
            requirements.Count, brdDocIds.Count, projectId);
        return requirements;
    }

    public async Task<List<Requirement>> ReadAllAsync(string basePath, CancellationToken ct = default)
    {
        var requirements = new List<Requirement>();
        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Requirements path {Path} does not exist", basePath);
            return requirements;
        }

        var files = Directory.EnumerateFiles(basePath, "*.*", SearchOption.AllDirectories)
            .Where(f => s_docExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var parsed = await ReadFileAsync(file, ct);
            requirements.AddRange(parsed);
        }

        _logger.LogInformation("Parsed {Count} requirements from {Path}", requirements.Count, basePath);
        return requirements;
    }

    public async Task<List<Requirement>> ReadFileAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return [];

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var sourceFile = Path.GetFileName(filePath);
        var module = InferModule(sourceFile);
        return ParseLines(lines, sourceFile, module);
    }

    private List<Requirement> ParseLines(string[] lines, string sourceFile, string module)
    {
        var results = new List<Requirement>();

        string currentSection = string.Empty;
        int currentLevel = 0;
        string currentTitle = string.Empty;
        var bodyLines = new List<string>();
        var tags = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var headingMatch = HeadingRegex().Match(line);

            if (headingMatch.Success)
            {
                // Flush previous section
                if (!string.IsNullOrWhiteSpace(currentTitle))
                {
                    TryAddRequirement(results, sourceFile, module, currentSection,
                        currentLevel, currentTitle, bodyLines, tags);
                }

                currentLevel = headingMatch.Groups[1].Value.Length;
                currentTitle = headingMatch.Groups[2].Value.Trim();
                currentSection = currentLevel <= 2 ? currentTitle : currentSection;
                bodyLines = [];
                tags = ExtractTags(currentTitle, module);
            }
            else
            {
                bodyLines.Add(line);
            }
        }

        // Flush last section
        if (!string.IsNullOrWhiteSpace(currentTitle))
        {
            TryAddRequirement(results, sourceFile, module, currentSection,
                currentLevel, currentTitle, bodyLines, tags);
        }

        return results;
    }

    private void TryAddRequirement(
        List<Requirement> results,
        string source,
        string module,
        string section,
        int level,
        string title,
        List<string> bodyLines,
        List<string> tags)
    {
        if (!IsCandidateRequirement(level, title, bodyLines))
            return;

        results.Add(BuildRequirement(source, module, section, level, title, bodyLines, tags));
    }

    private static bool IsCandidateRequirement(int level, string title, List<string> bodyLines)
    {
        if (level <= 0 || string.IsNullOrWhiteSpace(title))
            return false;

        // Deep headings are usually implementation notes or doc structure, not first-cut requirements.
        if (level > 4)
            return false;

        var trimmedTitle = title.Trim();
        var lowerTitle = trimmedTitle.ToLowerInvariant();
        if (s_structuralHeadings.Any(h => lowerTitle.Equals(h) || lowerTitle.StartsWith(h + " ")))
            return false;

        if (NumberedStructuralHeadingRegex().IsMatch(trimmedTitle))
            return false;

        var body = string.Join('\n', bodyLines).Trim();
        var bulletCount = bodyLines.Count(l => ListBulletRegex().IsMatch(l.TrimStart()));
        var hasContent = body.Length >= 40 || bulletCount > 0;
        if (!hasContent)
            return false;

        var lowerBody = body.ToLowerInvariant();
        var hasSignal = s_requirementSignals.Any(k => lowerTitle.Contains(k) || lowerBody.Contains(k));

        return hasSignal || bulletCount > 0;
    }

    private Requirement BuildRequirement(string source, string module, string section,
        int level, string title, List<string> bodyLines, List<string> tags)
    {
        var body = string.Join('\n', bodyLines).Trim();
        var criteria = ExtractAcceptanceCriteria(bodyLines);

        return new Requirement
        {
            Id = $"REQ-{Interlocked.Increment(ref _idCounter):D4}",
            SourceFile = source,
            Section = section,
            HeadingLevel = level,
            Title = title,
            Description = body,
            Module = module,
            Tags = tags,
            AcceptanceCriteria = criteria
        };
    }

    private static List<string> ExtractAcceptanceCriteria(List<string> bodyLines)
    {
        var criteria = new List<string>();
        var current = string.Empty;

        foreach (var rawLine in bodyLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrWhiteSpace(current))
                {
                    criteria.Add(current.Trim());
                    current = string.Empty;
                }
                continue;
            }

            var bulletMatch = ListBulletRegex().Match(line);
            if (bulletMatch.Success)
            {
                if (!string.IsNullOrWhiteSpace(current))
                    criteria.Add(current.Trim());

                current = bulletMatch.Groups[1].Value.Trim();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                // Preserve wrapped bullet/list content so one criterion is not fragmented by line breaks.
                current += " " + line;
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
            criteria.Add(current.Trim());

        criteria = criteria
            .Select(c => c.Trim())
            .Where(c => c.Length > 10)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        if (criteria.Count > 0)
            return criteria;

        var body = string.Join(' ', bodyLines).Trim();
        var fallback = SentenceRegex().Split(body)
            .Select(s => s.Trim())
            .Where(s => s.Length > 25 && s_requirementSignals.Any(sig => s.Contains(sig, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToList();

        return fallback;
    }

    private static string InferModule(string fileName)
    {
        if (fileName.Contains("requirement", StringComparison.OrdinalIgnoreCase)) return "Requirements";
        if (fileName.Contains("epic", StringComparison.OrdinalIgnoreCase)) return "Epics";
        if (fileName.Contains("architecture", StringComparison.OrdinalIgnoreCase)) return "Architecture";
        if (fileName.Contains("compliance", StringComparison.OrdinalIgnoreCase)) return "Compliance";
        if (fileName.Contains("schema", StringComparison.OrdinalIgnoreCase)) return "Schema";
        if (fileName.Contains("api", StringComparison.OrdinalIgnoreCase)) return "API";
        if (fileName.Contains("ai-", StringComparison.OrdinalIgnoreCase)) return "AI";
        if (fileName.Contains("database", StringComparison.OrdinalIgnoreCase)) return "Database";
        if (fileName.Contains("multi-tenant", StringComparison.OrdinalIgnoreCase)) return "MultiTenant";
        if (fileName.Contains("integration", StringComparison.OrdinalIgnoreCase)) return "Integration";
        if (fileName.Contains("test", StringComparison.OrdinalIgnoreCase)) return "Testing";
        return "General";
    }

    private static List<string> ExtractTags(string title, string module)
    {
        var tags = new List<string> { module };
        var lower = title.ToLowerInvariant();
        if (lower.Contains("patient")) tags.Add("Patient");
        if (lower.Contains("encounter")) tags.Add("Encounter");
        if (lower.Contains("admission") || lower.Contains("inpatient")) tags.Add("Inpatient");
        if (lower.Contains("emergency")) tags.Add("Emergency");
        if (lower.Contains("billing") || lower.Contains("revenue") || lower.Contains("claim")) tags.Add("Revenue");
        if (lower.Contains("pharmacy") || lower.Contains("medication")) tags.Add("Pharmacy");
        if (lower.Contains("diagnostic") || lower.Contains("lab") || lower.Contains("result")) tags.Add("Diagnostics");
        if (lower.Contains("ai") || lower.Contains("copilot") || lower.Contains("automation")) tags.Add("AI");
        if (lower.Contains("security") || lower.Contains("audit") || lower.Contains("compliance")) tags.Add("Security");
        if (lower.Contains("tenant") || lower.Contains("multi-tenant")) tags.Add("MultiTenant");
        if (lower.Contains("integration") || lower.Contains("api") || lower.Contains("adapter")) tags.Add("Integration");
        return tags.Distinct().ToList();
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?:[-*]|\d+[\.)])\s+(.+)$")]
    private static partial Regex ListBulletRegex();

    [GeneratedRegex(@"[.!?]+\s+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"^\d+(\.\d+)*\.?\s*(purpose|scope|references?|definitions?|glossary|revision history|table of contents)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedStructuralHeadingRegex();
}
