using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Requirements;

/// <summary>
/// Analyzes generated artifacts against requirements to:
/// 1. Compute coverage per requirement (which artifacts satisfy which acceptance criteria)
/// 2. Identify implementation gaps (missing services, untested flows, incomplete NFRs)
/// 3. Generate new user stories and use cases to close gaps
/// 4. Resolve requirement dependency chains so downstream agents build in correct order
/// 5. Feed gap-closing stories into the backlog via directives
///
/// Runs after the first generation cycle (database/service/application/integration/review)
/// so it has real artifacts to compare against.
/// </summary>
public sealed class RequirementAnalyzerAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<RequirementAnalyzerAgent> _logger;

    public AgentType Type => AgentType.RequirementAnalyzer;
    public string Name => "Requirement Analyzer";
    public string Description => "Gap-analysis engine — compares requirements to artifacts, identifies missing features, generates stories to close gaps.";

    public RequirementAnalyzerAgent(ILlmProvider llm, ILogger<RequirementAnalyzerAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("RequirementAnalyzer starting — {Reqs} requirements, {Arts} artifacts, {Exp} expanded items",
            context.Requirements.Count, context.Artifacts.Count, context.ExpandedRequirements.Count);

        var artifacts = context.Artifacts.ToList();
        var findings = context.Findings.ToList();
        var newStories = new List<ExpandedRequirement>();
        var reportSb = new StringBuilder();
        reportSb.AppendLine("# Requirement Gap Analysis Report");
        reportSb.AppendLine($"**Run**: {context.RunId} | **Iteration**: {context.DevIteration} | **Time**: {DateTime.UtcNow:u}");
        reportSb.AppendLine();

        // ─── Step 1: Build service→artifact index ───
        var artifactIndex = BuildArtifactIndex(artifacts);
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Built artifact index: {artifactIndex.Count} services, {artifacts.Count} total artifacts");
        reportSb.AppendLine("## Artifact Inventory");
        foreach (var (svc, layers) in artifactIndex.OrderBy(k => k.Key))
        {
            reportSb.AppendLine($"- **{svc}**: {string.Join(", ", layers.Select(l => $"{l.Key}({l.Value.Count})"))}");
        }
        reportSb.AppendLine();

        // ─── Step 2: Coverage analysis per requirement ───
        reportSb.AppendLine("## Coverage Analysis");
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Analyzing coverage for {context.Requirements.Count} requirements against {artifacts.Count} artifacts");
        var gapsByModule = new Dictionary<string, List<string>>();
        int coveredCount = 0, partialCount = 0, gapCount = 0;

        foreach (var req in context.Requirements)
        {
            var coverage = AnalyzeRequirementCoverage(req, artifacts, findings, context.DomainModel);
            var module = string.IsNullOrEmpty(req.Module) ? "General" : req.Module;

            // Update any expanded requirements linked to this requirement
            foreach (var exp in context.ExpandedRequirements.Where(e => e.SourceRequirementId == req.Id))
            {
                exp.Coverage = coverage.Status;
                exp.IdentifiedGaps = coverage.Gaps;
                exp.MatchingArtifactPaths = coverage.MatchingPaths;
                exp.AffectedServices = coverage.AffectedServices;
            }

            switch (coverage.Status)
            {
                case CoverageStatus.Covered: coveredCount++; break;
                case CoverageStatus.Partial: partialCount++; break;
                default: gapCount++; break;
            }

            if (coverage.Gaps.Count > 0)
            {
                if (!gapsByModule.ContainsKey(module)) gapsByModule[module] = [];
                gapsByModule[module].AddRange(coverage.Gaps);
                reportSb.AppendLine($"- **{req.Id}** ({req.Title}): `{coverage.Status}` — {coverage.Gaps.Count} gaps");
                foreach (var g in coverage.Gaps.Take(5))
                    reportSb.AppendLine($"  - {g}");
            }
        }
        reportSb.AppendLine();
        reportSb.AppendLine($"**Summary**: {coveredCount} covered, {partialCount} partial, {gapCount} gaps out of {context.Requirements.Count} requirements");
        reportSb.AppendLine();

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Coverage: {coveredCount} covered, {partialCount} partial, {gapCount} gaps — {gapsByModule.Count} modules with gaps");

        // ─── Step 3: Cross-cutting gap detection ───
        reportSb.AppendLine("## Cross-Cutting Gap Detection");
        var crossCuttingGaps = DetectCrossCuttingGaps(artifacts, findings, context.DomainModel);
        foreach (var gap in crossCuttingGaps)
            reportSb.AppendLine($"- {gap}");
        reportSb.AppendLine();

        // Merge cross-cutting gaps into modules
        if (crossCuttingGaps.Count > 0)
        {
            if (!gapsByModule.ContainsKey("CrossCutting")) gapsByModule["CrossCutting"] = [];
            gapsByModule["CrossCutting"].AddRange(crossCuttingGaps);
        }

        // ─── Step 4: LLM-powered gap→story generation ───
        reportSb.AppendLine("## Generated Stories to Close Gaps");
        foreach (var (module, gaps) in gapsByModule)
        {
            if (gaps.Count == 0) continue;
            ct.ThrowIfCancellationRequested();

            var stories = await GenerateGapClosingStories(module, gaps, context, ct);
            newStories.AddRange(stories);

            foreach (var s in stories)
                reportSb.AppendLine($"- [{s.ItemType}] **{s.Id}**: {s.Title}");
        }
        reportSb.AppendLine();

        // ─── Step 5: Dependency chain resolution ───
        reportSb.AppendLine("## Dependency Resolution");
        ResolveDependencyChains(context.ExpandedRequirements, newStories, context.DomainModel);
        reportSb.AppendLine($"- Resolved dependency chains for {newStories.Count + context.ExpandedRequirements.Count} items");
        reportSb.AppendLine();

        // ─── Step 6: Feed into backlog via directives ───
        var existingIds = context.ExpandedRequirements.Select(e => e.Id).ToHashSet();
        int addedCount = 0;
        foreach (var story in newStories)
        {
            if (!existingIds.Contains(story.Id))
            {
                context.ExpandedRequirements.Add(story);
                existingIds.Add(story.Id);
                addedCount++;
            }
        }

        // Send directive to Backlog to refresh
        if (addedCount > 0)
        {
            context.DirectiveQueue.Enqueue(new AgentDirective
            {
                From = Type,
                To = AgentType.Backlog,
                Action = "REFRESH_BACKLOG",
                Details = $"RequirementAnalyzer added {addedCount} gap-closing stories for iteration {context.DevIteration}",
                Priority = 1
            });
        }

        reportSb.AppendLine($"## Totals: {addedCount} new stories added to backlog");

        // Produce report artifact
        var reportArtifact = new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = $"analysis/gap-analysis-iter{context.DevIteration}.md",
            FileName = $"gap-analysis-iter{context.DevIteration}.md",
            Namespace = string.Empty,
            ProducedBy = Type,
            TracedRequirementIds = ["NFR-COVERAGE-01"],
            Content = reportSb.ToString()
        };
        context.Artifacts.Add(reportArtifact);

        context.AgentStatuses[Type] = AgentStatus.Completed;
        _logger.LogInformation("RequirementAnalyzer completed — {Covered} covered, {Partial} partial, {Gaps} gaps → {Added} new stories",
            coveredCount, partialCount, gapCount, addedCount);

        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Gap Analysis: {coveredCount}/{context.Requirements.Count} covered, {gapCount} gaps → {addedCount} new stories",
            Artifacts = [reportArtifact],
            Duration = sw.Elapsed
        };
    }

    // ─── Coverage Analysis Per Requirement ─────────────────────────────
    private sealed record CoverageResult(
        CoverageStatus Status,
        List<string> Gaps,
        List<string> MatchingPaths,
        List<string> AffectedServices);

    private CoverageResult AnalyzeRequirementCoverage(Requirement req, List<CodeArtifact> artifacts,
        List<ReviewFinding> findings, ParsedDomainModel? domainModel)
    {
        var gaps = new List<string>();
        var matchingPaths = new List<string>();
        var affectedServices = new List<string>();

        // Identify which services/entities this requirement touches
        var tags = req.Tags;
        var title = req.Title.ToLowerInvariant();
        var desc = req.Description.ToLowerInvariant();

        foreach (var svc in MicroserviceCatalog.All)
        {
            var svcLower = svc.Name.ToLowerInvariant().Replace("service", "");
            if (tags.Any(t => t.Equals(svc.ShortName, StringComparison.OrdinalIgnoreCase)) ||
                title.Contains(svcLower) || desc.Contains(svcLower) ||
                svc.Entities.Any(e => title.Contains(e.ToLowerInvariant()) || desc.Contains(e.ToLowerInvariant())))
            {
                affectedServices.Add(svc.Name);
            }
        }

        // If no specific service detected, infer from module
        if (affectedServices.Count == 0)
        {
            var svc = InferServiceFromModule(req.Module);
            if (svc is not null) affectedServices.Add(svc);
        }

        // Check artifact layers per affected service
        foreach (var svcName in affectedServices)
        {
            var svcArtifacts = artifacts.Where(a =>
                a.Namespace.Contains(svcName.Replace("Service", ""), StringComparison.OrdinalIgnoreCase) ||
                a.RelativePath.Contains(svcName.Replace("Service", ""), StringComparison.OrdinalIgnoreCase)).ToList();

            // Check: Database layer
            if (!svcArtifacts.Any(a => a.Layer == ArtifactLayer.Database))
                gaps.Add($"Missing database entities for {svcName}");
            else
                matchingPaths.AddRange(svcArtifacts.Where(a => a.Layer == ArtifactLayer.Database).Select(a => a.RelativePath));

            // Check: Service layer
            if (!svcArtifacts.Any(a => a.Layer == ArtifactLayer.Service))
                gaps.Add($"Missing service implementation for {svcName}");
            else
                matchingPaths.AddRange(svcArtifacts.Where(a => a.Layer == ArtifactLayer.Service).Select(a => a.RelativePath));

            // Check: Repository layer
            if (!svcArtifacts.Any(a => a.Layer == ArtifactLayer.Repository))
                gaps.Add($"Missing repository layer for {svcName}");

            // Check: DTO layer
            if (!svcArtifacts.Any(a => a.Layer == ArtifactLayer.Dto))
                gaps.Add($"Missing DTOs for {svcName}");

            // Check: Test coverage
            if (!svcArtifacts.Any(a => a.Layer == ArtifactLayer.Test))
                gaps.Add($"Missing unit tests for {svcName}");

            // Check: API/ViewModel layer for user-facing requirements
            if ((title.Contains("api") || title.Contains("endpoint") || title.Contains("portal")) &&
                !svcArtifacts.Any(a => a.Layer is ArtifactLayer.ViewModel or ArtifactLayer.RazorPage))
                gaps.Add($"Missing API/UI layer for {svcName}");
        }

        // Check acceptance criteria coverage
        foreach (var ac in req.AcceptanceCriteria)
        {
            var acLower = ac.ToLowerInvariant();
            // Look for artifacts that reference this criterion
            if (!artifacts.Any(a => a.Content.Contains(ac, StringComparison.OrdinalIgnoreCase) ||
                                    a.TracedRequirementIds.Contains(req.Id)))
            {
                // Check if Findings / implementation covers it heuristically
                var keywords = ExtractKeywords(ac);
                if (!artifacts.Any(a => keywords.Any(k => a.Content.Contains(k, StringComparison.OrdinalIgnoreCase))))
                    gaps.Add($"Acceptance criterion not covered: \"{Truncate(ac, 80)}\"");
            }
        }

        // Check NFR compliance from domain model
        if (domainModel is not null)
        {
            foreach (var nfr in domainModel.NfrRequirements)
            {
                if (req.Tags.Contains(nfr.Category) || req.Module == nfr.Category)
                {
                    // Check if there's a finding or artifact addressing this NFR
                    if (!artifacts.Any(a => a.TracedRequirementIds.Contains(nfr.Id)))
                        gaps.Add($"NFR not addressed: {nfr.Id} — {nfr.Description}");
                }
            }
        }

        // Open error-level findings against matched artifacts indicate partial coverage
        var hasOpenErrors = findings.Any(f =>
            f.Severity >= ReviewSeverity.Error &&
            matchingPaths.Any(p => f.FilePath?.Contains(Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase) ?? false));

        if (hasOpenErrors) gaps.Add("Open error-level review findings against implemented artifacts");

        var status = gaps.Count == 0
            ? (matchingPaths.Count > 0 ? CoverageStatus.Covered : CoverageStatus.NotStarted)
            : (matchingPaths.Count > 0 ? CoverageStatus.Partial : CoverageStatus.GapIdentified);

        return new CoverageResult(status, gaps, matchingPaths.Distinct().ToList(), affectedServices);
    }

    // ─── Cross-Cutting Gap Detection ───────────────────────────────────
    private static List<string> DetectCrossCuttingGaps(List<CodeArtifact> artifacts,
        List<ReviewFinding> findings, ParsedDomainModel? domainModel)
    {
        var gaps = new List<string>();
        var artifactsByLayer = artifacts.GroupBy(a => a.Layer).ToDictionary(g => g.Key, g => g.ToList());

        // 1. Missing integration tests
        if (!artifactsByLayer.ContainsKey(ArtifactLayer.Integration) ||
            artifactsByLayer[ArtifactLayer.Integration].Count < MicroserviceCatalog.All.Length)
            gaps.Add("Missing integration layer for some services — inter-service communication may fail");

        // 2. Missing security artifacts
        if (!artifactsByLayer.ContainsKey(ArtifactLayer.Security))
            gaps.Add("No security artifacts generated — authentication/authorization not implemented");

        // 3. Missing compliance artifacts
        if (!artifactsByLayer.ContainsKey(ArtifactLayer.Compliance))
            gaps.Add("No compliance artifacts — HIPAA/SOC2 controls not generated");

        // 4. Missing observability
        if (!artifactsByLayer.ContainsKey(ArtifactLayer.Observability))
            gaps.Add("No observability artifacts — missing health checks, metrics, and logging infrastructure");

        // 5. Service-to-service dependency gaps
        if (domainModel is not null)
        {
            foreach (var svc in MicroserviceCatalog.All)
            {
                foreach (var dep in svc.DependsOn)
                {
                    var hasIntegration = artifacts.Any(a =>
                        a.Layer == ArtifactLayer.Integration &&
                        a.Namespace.Contains(svc.Name.Replace("Service", ""), StringComparison.OrdinalIgnoreCase) &&
                        a.Content.Contains(dep, StringComparison.OrdinalIgnoreCase));

                    if (!hasIntegration)
                        gaps.Add($"{svc.Name} depends on {dep} but no integration client found");
                }
            }
        }

        // 6. Missing migration artifacts
        if (!artifactsByLayer.ContainsKey(ArtifactLayer.Migration))
            gaps.Add("No database migrations generated — schema changes cannot be applied");

        // 7. Missing configuration per environment
        if (!artifactsByLayer.ContainsKey(ArtifactLayer.Configuration))
            gaps.Add("No configuration artifacts — missing per-environment appsettings");

        // 8. Unresolved critical findings
        var criticalFindings = findings.Where(f => f.Severity >= ReviewSeverity.Critical).ToList();
        if (criticalFindings.Count > 0)
            gaps.Add($"{criticalFindings.Count} unresolved critical/security findings need stories to remediate");

        return gaps;
    }

    // ─── LLM-powered Story Generation ──────────────────────────────────
    private async Task<List<ExpandedRequirement>> GenerateGapClosingStories(
        string module, List<string> gaps, AgentContext context, CancellationToken ct)
    {
        var gapBlock = string.Join("\n", gaps.Distinct().Take(30).Select((g, i) => $"  {i + 1}. {g}"));

        // Existing stories to avoid duplicates
        var existingTitles = context.ExpandedRequirements
            .Where(e => e.Module == module || module == "CrossCutting")
            .Select(e => e.Title)
            .Take(30)
            .ToList();
        var existingBlock = string.Join("\n", existingTitles.Select(t => $"  - {t}"));

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a senior healthcare software architect and requirements analyst.
                You write implementation-ready user stories and use cases for a Hospital Management System.
                
                Each story must be specific enough for a developer to implement:
                - Include acceptance criteria that map to testable conditions
                - Specify which microservice(s) are affected
                - Include data model hints (entities, fields, relationships)
                - Include API contract expectations (method, path, request/response shape)
                - Specify validation rules and business constraints
                - Note HIPAA/compliance implications if any
                - Declare dependencies on other stories/features
                
                Output EXACTLY in this format — one item per line:
                STORY|<id>|<parent>|<title>|<acceptance_criteria>|<priority 1-3>|<services_csv>|<depends_on_csv>|<spec>
                USECASE|<id>|<parent>|<title>|<preconditions>|<main_flow>|<alt_flows>|<postconditions>|<services_csv>
                TASK|<id>|<parent>|<title>|<tags_csv>|<priority 1-3>|<services_csv>|<detailed_spec>
                
                Use IDs like GAP-<MODULE>-<SEQ> (e.g. GAP-PAT-001).
                """,
            UserPrompt = $"""
                Module: {module}
                Iteration: {context.DevIteration}
                
                === IDENTIFIED GAPS ===
                {gapBlock}
                
                === ALREADY IN BACKLOG (don't duplicate) ===
                {existingBlock}
                
                === MICROSERVICE CATALOG ===
                {string.Join("\n", MicroserviceCatalog.All.Select(s => $"  - {s.Name} ({s.Schema}): entities=[{string.Join(",", s.Entities)}], depends=[{string.Join(",", s.DependsOn)}]"))}
                
                Generate user stories, use cases, and tasks to close ALL identified gaps.
                Be specific — downstream agents will generate code directly from your specifications.
                Include database schema details, API contracts, validation rules, and test scenarios.
                """,
            Temperature = 0.3,
            MaxTokens = 4096,
            RequestingAgent = Name
        };

        var response = await _llm.GenerateAsync(prompt, ct);
        if (!response.Success)
        {
            _logger.LogWarning("LLM story generation failed for {Module}: {Error}", module, response.Error);
            return CreateFallbackGapStories(module, gaps, context.DevIteration);
        }

        return ParseGapStories(response.Content, module, context.DevIteration);
    }

    private static List<ExpandedRequirement> ParseGapStories(string content, string module, int iteration)
    {
        var items = new List<ExpandedRequirement>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 5) continue;

            var kind = parts[0].Trim().ToUpperInvariant();
            switch (kind)
            {
                case "STORY" when parts.Length >= 8:
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.UserStory,
                        Title = parts[3].Trim(),
                        Description = parts[4].Trim(),
                        Module = module,
                        Priority = ParsePriority(parts[5]),
                        Iteration = iteration,
                        AcceptanceCriteria = [parts[4].Trim()],
                        AffectedServices = [.. parts[6].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                        DependsOn = [.. parts[7].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                        DetailedSpec = parts.Length > 8 ? parts[8].Trim() : "",
                        Status = WorkItemStatus.New,
                        Coverage = CoverageStatus.GapIdentified,
                        ProducedBy = "RequirementAnalyzer"
                    });
                    break;

                case "USECASE" when parts.Length >= 8:
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.UseCase,
                        Title = parts[3].Trim(),
                        Description = $"Preconditions: {parts[4].Trim()}\nMain Flow: {parts[5].Trim()}\nAlternative Flows: {parts[6].Trim()}\nPostconditions: {parts[7].Trim()}",
                        Module = module,
                        Priority = 2,
                        Iteration = iteration,
                        AffectedServices = parts.Length > 8
                            ? [.. parts[8].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        Status = WorkItemStatus.New,
                        Coverage = CoverageStatus.GapIdentified,
                        ProducedBy = "RequirementAnalyzer"
                    });
                    break;

                case "TASK" when parts.Length >= 6:
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.Task,
                        Title = parts[3].Trim(),
                        Module = module,
                        Priority = ParsePriority(parts[5]),
                        Iteration = iteration,
                        Tags = [.. parts[4].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                        AffectedServices = parts.Length > 6
                            ? [.. parts[6].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DetailedSpec = parts.Length > 7 ? parts[7].Trim() : "",
                        Status = WorkItemStatus.New,
                        Coverage = CoverageStatus.GapIdentified,
                        ProducedBy = "RequirementAnalyzer"
                    });
                    break;
            }
        }

        return items;
    }

    private static List<ExpandedRequirement> CreateFallbackGapStories(string module, List<string> gaps, int iteration)
    {
        var items = new List<ExpandedRequirement>();
        int seq = 0;

        foreach (var gap in gaps.Distinct().Take(20))
        {
            seq++;
            items.Add(new ExpandedRequirement
            {
                Id = $"GAP-{module}-{seq:D3}",
                ItemType = WorkItemType.UserStory,
                Title = $"Close gap: {Truncate(gap, 100)}",
                Description = gap,
                Module = module,
                Priority = gap.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
                           gap.Contains("security", StringComparison.OrdinalIgnoreCase) ? 1 : 2,
                Iteration = iteration,
                Status = WorkItemStatus.New,
                Coverage = CoverageStatus.GapIdentified,
                ProducedBy = "RequirementAnalyzer"
            });
        }

        return items;
    }

    // ─── Dependency Chain Resolution ───────────────────────────────────
    private static void ResolveDependencyChains(List<ExpandedRequirement> existing,
        List<ExpandedRequirement> newItems, ParsedDomainModel? domainModel)
    {
        var all = existing.Concat(newItems).ToList();
        var byId = all.ToDictionary(e => e.Id);

        foreach (var item in all)
        {
            var chain = new List<string>();
            ResolveChainRecursive(item, byId, chain, depth: 0);
            item.ResolvedDependencyChain = chain;

            // Auto-detect service dependencies from affected services
            if (item.AffectedServices.Count > 0 && domainModel is not null)
            {
                foreach (var svcName in item.AffectedServices)
                {
                    var svc = MicroserviceCatalog.ByName(svcName);
                    if (svc is null) continue;

                    foreach (var dep in svc.DependsOn)
                    {
                        // Find items in the backlog that implement the dependent service
                        var depItems = all.Where(e =>
                            e.AffectedServices.Any(s => s.Equals(dep, StringComparison.OrdinalIgnoreCase)) &&
                            e.ItemType is WorkItemType.Epic or WorkItemType.UserStory &&
                            e.Id != item.Id);

                        foreach (var depItem in depItems)
                        {
                            if (!item.DependsOn.Contains(depItem.Id) && !chain.Contains(depItem.Id))
                                item.ResolvedDependencyChain.Add(depItem.Id);
                        }
                    }
                }
            }
        }
    }

    private static void ResolveChainRecursive(ExpandedRequirement item,
        Dictionary<string, ExpandedRequirement> byId, List<string> chain, int depth)
    {
        if (depth > 10) return; // prevent cycles

        foreach (var depId in item.DependsOn)
        {
            if (chain.Contains(depId)) continue;
            chain.Add(depId);

            if (byId.TryGetValue(depId, out var dep))
                ResolveChainRecursive(dep, byId, chain, depth + 1);
        }
    }

    // ─── Artifact Indexing ─────────────────────────────────────────────
    private static Dictionary<string, Dictionary<ArtifactLayer, List<CodeArtifact>>> BuildArtifactIndex(
        List<CodeArtifact> artifacts)
    {
        var index = new Dictionary<string, Dictionary<ArtifactLayer, List<CodeArtifact>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var art in artifacts)
        {
            var svc = ExtractServiceName(art);
            if (!index.ContainsKey(svc)) index[svc] = new Dictionary<ArtifactLayer, List<CodeArtifact>>();
            if (!index[svc].ContainsKey(art.Layer)) index[svc][art.Layer] = [];
            index[svc][art.Layer].Add(art);
        }

        return index;
    }

    private static string ExtractServiceName(CodeArtifact art)
    {
        // Try namespace first
        var parts = art.Namespace.Split('.');
        if (parts.Length >= 2) return parts[1]; // e.g. Hms.PatientService → PatientService

        // Try path
        foreach (var svc in MicroserviceCatalog.All)
        {
            if (art.RelativePath.Contains(svc.Name, StringComparison.OrdinalIgnoreCase) ||
                art.RelativePath.Contains(svc.ShortName, StringComparison.OrdinalIgnoreCase))
                return svc.Name;
        }

        return "Shared";
    }

    private static string? InferServiceFromModule(string module)
    {
        return module.ToLowerInvariant() switch
        {
            "requirements" or "epics" or "general" => null,
            var m when m.Contains("patient") => "PatientService",
            var m when m.Contains("encounter") || m.Contains("opd") => "EncounterService",
            var m when m.Contains("inpatient") || m.Contains("admission") => "InpatientService",
            var m when m.Contains("emergency") => "EmergencyService",
            var m when m.Contains("diagnostic") || m.Contains("lab") => "DiagnosticsService",
            var m when m.Contains("revenue") || m.Contains("billing") => "RevenueService",
            var m when m.Contains("audit") || m.Contains("compliance") => "AuditService",
            var m when m.Contains("ai") => "AiService",
            _ => null
        };
    }

    private static List<string> ExtractKeywords(string text)
    {
        // Extract meaningful 3+ char words, skipping common stop words
        var stopWords = new HashSet<string> { "the", "and", "for", "with", "that", "this", "from", "are", "was", "has", "can", "all", "each" };
        return Regex.Matches(text, @"\b[a-zA-Z]{3,}\b")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => !stopWords.Contains(w))
            .Distinct()
            .Take(10)
            .ToList();
    }

    private static string SanitizeId(string id) =>
        Regex.Replace(id.Trim(), @"[^a-zA-Z0-9\-_]", "");

    private static int ParsePriority(string s) =>
        int.TryParse(s.Trim(), out var p) ? Math.Clamp(p, 1, 3) : 2;

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
