using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Requirements;

/// <summary>
/// Iteratively expands high-level requirements into detailed Epics, User Stories,
/// Use Cases, and Tasks using the LLM. Produces implementation-ready specifications
/// with dependency chains, affected services, and detailed specs so downstream agents
/// (Database, ServiceLayer, Application, Testing) can generate quality code.
/// </summary>
public sealed class RequirementsExpanderAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<RequirementsExpanderAgent> _logger;
    public AgentType Type => AgentType.RequirementsExpander;
    public string Name => "Requirements Expander";
    public string Description => "Expands high-level requirements into implementation-ready epics, user stories, use cases, and tasks with dependency chains and detailed specs.";

    public RequirementsExpanderAgent(ILlmProvider llm, ILogger<RequirementsExpanderAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("RequirementsExpander starting — {Count} base requirements, iteration {Iter}",
            context.Requirements.Count, context.DevIteration);

        try
        {
        var expanded = new List<ExpandedRequirement>();
        var iteration = context.DevIteration;

        // ── Step 0: LLM-based quality assessment (informational — never blocks expansion) ──
        var allRequirements = context.Requirements.ToList();
        var qualityInsights = await RunLlmQualityAssessmentAsync(allRequirements, ct);

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"LLM quality assessment complete: {allRequirements.Count} requirements evaluated — {qualityInsights.ReadyCount} strong, {qualityInsights.NeedsWorkCount} need enrichment during expansion");

        // Emit scorecard artifact for reviewers
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "Requirements/requirement-quality-scorecard.md",
            FileName = "requirement-quality-scorecard.md",
            Namespace = "GNex.Requirements",
            ProducedBy = Type,
            Content = qualityInsights.ScorecardMarkdown
        });

        // Emit findings for low-quality items (informational only)
        foreach (var issue in qualityInsights.Issues.Take(50))
        {
            context.Findings.Add(new ReviewFinding
            {
                Category = "RequirementQuality",
                Severity = ReviewSeverity.Info,
                Message = issue,
                FilePath = "docs/requirements",
                Suggestion = "The LLM will enrich this requirement during expansion. Review the expanded output for completeness."
            });
        }

        // ALL requirements proceed to expansion — no blocking gate
        var readyRequirements = allRequirements;

        // ── Step 1: Build requirement→service mapping ──
        var reqServiceMap = BuildRequirementServiceMap(readyRequirements, context.DomainModel, context);
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Mapped {readyRequirements.Count} requirements to {reqServiceMap.Values.SelectMany(v => v).Distinct().Count()} microservices");

        // ── Step 2: Build requirement dependency graph ──
        var reqDeps = BuildRequirementDependencies(readyRequirements, reqServiceMap, context);
        if (context.ReportProgress is not null)
        {
            var totalDeps = reqDeps.Values.Sum(d => d.Count);
            await context.ReportProgress(Type, $"Built dependency graph: {totalDeps} cross-requirement dependencies identified");
        }

        // ── Step 3: Group by module and expand ──
        var byModule = readyRequirements
            .GroupBy(r => string.IsNullOrEmpty(r.Module) ? "General" : r.Module)
            .ToList();

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Expanding {byModule.Count} modules: {string.Join(", ", byModule.Select(g => $"{g.Key}({g.Count()})").Take(10))}");

        foreach (var moduleGroup in byModule)
        {
            ct.ThrowIfCancellationRequested();
            var module = moduleGroup.Key;
            var reqs = moduleGroup.ToList();

            if (context.ReportProgress is not null)
            {
                var svcNames = reqs.SelectMany(r => reqServiceMap.TryGetValue(r.Id, out var s) ? s : []).Distinct().ToList();
                await context.ReportProgress(Type, $"Expanding module '{module}': {reqs.Count} requirements → services: {string.Join(", ", svcNames.Take(5))}");
            }

            // ── Recursive multi-phase expansion ──
            // Phase 1: Requirements → Epics (batched, 10 per LLM call)
            // Phase 2: Each Epic → Stories + Use Cases (1-2 per LLM call)
            // Phase 3: Each Story → Tasks (2-3 per LLM call)
            // Phase 4: Recursive refinement — split large tasks until atomic
            var domainCtx = BuildDomainContextForModule(module, context.DomainModel);
            var phaseItems = await ExpandModuleWithPhasesAsync(
                reqs, module, iteration, reqServiceMap, reqDeps, qualityInsights,
                domainCtx, context.ReportProgress, context, ct);

            await EnrichItemsWithLlmAsync(phaseItems, reqs, module, reqServiceMap, context, ct);

            if (context.ReportProgress is not null)
            {
                var ec = phaseItems.Count(i => i.ItemType == WorkItemType.Epic);
                var sc = phaseItems.Count(i => i.ItemType == WorkItemType.UserStory);
                var ucc = phaseItems.Count(i => i.ItemType == WorkItemType.UseCase);
                var tc = phaseItems.Count(i => i.ItemType == WorkItemType.Task);
                await context.ReportProgress(Type, $"Module '{module}' fully expanded: {ec} epics, {sc} stories, {ucc} use cases, {tc} tasks — {phaseItems.Count} total items");
            }

            expanded.AddRange(phaseItems);
        }

        // ── Step 3b: Deduplicate expanded items (same ID can appear from overlapping modules/re-runs) ──
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        expanded = expanded.Where(i => seenIds.Add(i.Id)).ToList();

        // ── Step 4: Resolve dependency chains across all expanded items ──
        ResolveExpandedDependencies(expanded, context.DomainModel, context);
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Resolved dependency chains across {expanded.Count} expanded items");

        // ── Step 5: Merge into context ──
        var existingIds = context.ExpandedRequirements.Select(e => e.Id).ToHashSet();
        foreach (var item in expanded)
        {
            if (!existingIds.Contains(item.Id))
            {
                context.ExpandedRequirements.Add(item);
                existingIds.Add(item.Id);
            }
        }

        // Send directive to Backlog and Analyzer
        context.DirectiveQueue.Enqueue(new AgentDirective
        {
            From = Type,
            To = AgentType.Backlog,
            Action = "REFRESH_BACKLOG",
            Details = $"Added {expanded.Count} expanded items for iteration {iteration}"
        });

        context.AgentStatuses[Type] = AgentStatus.Completed;

        var epicCount = expanded.Count(i => i.ItemType == WorkItemType.Epic);
        var storyCount = expanded.Count(i => i.ItemType == WorkItemType.UserStory);
        var ucCount = expanded.Count(i => i.ItemType == WorkItemType.UseCase);
        var taskCount = expanded.Count(i => i.ItemType == WorkItemType.Task);

        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Expanded: {expanded.Count} items ({epicCount} epics, {storyCount} stories, {ucCount} use cases, {taskCount} tasks) with dependency chains + specs",
            Duration = sw.Elapsed
        };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RequirementsExpander failed — {ExType}: {Message}", ex.GetType().Name, ex.Message);
            context.AgentStatuses[Type] = AgentStatus.Failed;
            return new AgentResult
            {
                Agent = Type, Success = false,
                Errors = [ex.ToString()],
                Summary = $"RequirementsExpander failed: {ex.GetType().Name}: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // RECURSIVE MULTI-PHASE EXPANSION
    // Phase 0: Enrich weak/incomplete requirements using LLM + domain knowledge
    // Phase 1: Requirements → Epics (batched, ~5 reqs per LLM call)
    // Phase 2: Epics → Stories + Use Cases (1-3 epics per LLM call)
    // Phase 3: Stories → Tasks (2-5 stories per LLM call)
    // Phase 4: Recursive refinement — split any task >8h until atomic
    // ═══════════════════════════════════════════════════════════════════

    private const int Phase0BatchSize = 10;
    private const int Phase1BatchSize = 5;
    private const int Phase2BatchSize = 3;
    private const int Phase3BatchSize = 5;
    private const int MaxRefinementDepth = 5;

    private async Task<List<ExpandedRequirement>> ExpandModuleWithPhasesAsync(
        List<Requirement> reqs,
        string module,
        int iteration,
        Dictionary<string, List<string>> reqServiceMap,
        Dictionary<string, List<string>> reqDeps,
        QualityAssessmentResult qualityInsights,
        string domainCtx,
        Func<AgentType, string, Task>? reportProgress,
        AgentContext context,
        CancellationToken ct)
    {
        var allItems = new List<ExpandedRequirement>();

        // ── Phase 0: Enrich requirements using LLM + domain knowledge ──
        if (reportProgress is not null)
            await reportProgress(Type, $"[Phase 0/5] Enriching {reqs.Count} requirements using domain expertise and quality assessment...");

        var enrichedDescriptions = await Phase0_EnrichRequirementsAsync(
            reqs, module, qualityInsights, domainCtx, reportProgress, ct);
        _logger.LogInformation("Phase 0 complete for '{Module}': {Enriched}/{Total} requirements enriched",
            module, enrichedDescriptions.Count, reqs.Count);

        // ── Phase 1: Requirements → Epics ──
        if (reportProgress is not null)
            await reportProgress(Type, $"[Phase 1/5] Generating Epics from {reqs.Count} enriched requirements in module '{module}'...");

        var epics = await Phase1_RequirementsToEpicsAsync(
            reqs, module, iteration, reqServiceMap, reqDeps, qualityInsights, domainCtx, enrichedDescriptions, reportProgress, ct);
        allItems.AddRange(epics);
        _logger.LogInformation("Phase 1 complete for '{Module}': {Count} epics generated", module, epics.Count);

        if (epics.Count == 0)
        {
            _logger.LogWarning("Phase 1 produced 0 epics for module '{Module}' — using fallback", module);
            return CreateFallbackItems(reqs, module, iteration, reqServiceMap, reqDeps, context);
        }

        // ── Phase 2: Epics → Stories + Use Cases ──
        if (reportProgress is not null)
            await reportProgress(Type, $"[Phase 2/5] Splitting {epics.Count} Epics into User Stories & Use Cases...");

        var storiesAndUseCases = await Phase2_EpicsToStoriesAsync(
            epics, reqs, module, iteration, domainCtx, reportProgress, ct);
        allItems.AddRange(storiesAndUseCases);

        var storyCount = storiesAndUseCases.Count(i => i.ItemType == WorkItemType.UserStory);
        var ucCount = storiesAndUseCases.Count(i => i.ItemType == WorkItemType.UseCase);
        _logger.LogInformation("Phase 2 complete for '{Module}': {Stories} stories, {UseCases} use cases", module, storyCount, ucCount);

        // ── Phase 3: Stories → Tasks ──
        var stories = storiesAndUseCases.Where(i => i.ItemType == WorkItemType.UserStory).ToList();
        if (reportProgress is not null)
            await reportProgress(Type, $"[Phase 3/5] Decomposing {stories.Count} Stories into implementation Tasks...");

        var tasks = await Phase3_StoriesToTasksAsync(
            stories, epics, reqs, module, iteration, domainCtx, reportProgress, ct);
        allItems.AddRange(tasks);
        _logger.LogInformation("Phase 3 complete for '{Module}': {Count} tasks generated", module, tasks.Count);

        // ── Phase 4: Recursive task refinement — split large/vague tasks ──
        if (reportProgress is not null)
            await reportProgress(Type, $"[Phase 4/5] Refining {tasks.Count} tasks — splitting any that are too large or vague...");

        var refined = await Phase4_RecursiveTaskRefinementAsync(
            tasks, module, iteration, domainCtx, reportProgress, ct, depth: 0);

        // Replace original tasks with refined set
        allItems.RemoveAll(i => i.ItemType == WorkItemType.Task);
        allItems.AddRange(refined);

        if (reportProgress is not null)
        {
            var finalEpics = allItems.Count(i => i.ItemType == WorkItemType.Epic);
            var finalStories = allItems.Count(i => i.ItemType == WorkItemType.UserStory);
            var finalUc = allItems.Count(i => i.ItemType == WorkItemType.UseCase);
            var finalTasks = allItems.Count(i => i.ItemType == WorkItemType.Task);
            await reportProgress(Type, $"All phases complete for '{module}': {finalEpics} epics, {finalStories} stories, {finalUc} use cases, {finalTasks} tasks = {allItems.Count} total");
        }

        return allItems;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PHASE 0: Requirement Enrichment
    // Before creating epics, enrich every requirement using the LLM's
    // domain knowledge. The quality scorecard tells us what each req
    // is missing (actors, measurable outcomes, acceptance criteria, etc.).
    // The LLM fills gaps with industry best practices and standards.
    // Output: Dictionary<reqId, enrichedDescription> fed into Phase 1.
    // ═══════════════════════════════════════════════════════════════════

    private async Task<Dictionary<string, string>> Phase0_EnrichRequirementsAsync(
        List<Requirement> reqs,
        string module,
        QualityAssessmentResult qualityInsights,
        string domainCtx,
        Func<AgentType, string, Task>? reportProgress,
        CancellationToken ct)
    {
        var enriched = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var batches = reqs.Chunk(Phase0BatchSize).ToList();

        for (var b = 0; b < batches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = batches[b].ToList();

            if (reportProgress is not null)
                await reportProgress(Type, $"  Phase 0 batch {b + 1}/{batches.Count}: enriching {batch.Count} requirements with domain expertise...");

            var reqBlocks = batch.Select(r =>
            {
                var qualityNote = qualityInsights.QualityHints.TryGetValue(r.Id, out var hint) ? hint : "No assessment available";
                return $"""
                    --- REQUIREMENT [{r.Id}] ---
                    Title: {r.Title}
                    Original Description: {Truncate(r.Description, 600)}
                    Acceptance Criteria: {(r.AcceptanceCriteria.Count > 0 ? string.Join("; ", r.AcceptanceCriteria) : "NONE — must be added")}
                    Tags: {(r.Tags.Count > 0 ? string.Join(", ", r.Tags) : "None")}
                    Dependencies: {(r.DependsOn.Count > 0 ? string.Join(", ", r.DependsOn) : "None")}
                    Quality Assessment: {qualityNote}
                    """;
            });

            var prompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a senior business analyst and domain expert with deep knowledge of
                    software systems, industry standards, compliance frameworks, and real-world
                    implementations. Your job is to ENRICH weak or incomplete requirements so
                    they can become high-quality epics.

                    For each requirement you receive, you will see:
                    - The original title and description from the BRD
                    - A quality assessment noting what is missing or weak
                    - The project's domain model for context

                    YOUR TASK for EACH requirement:
                    1. ANALYZE what is missing or weak based on the quality assessment
                    2. USE YOUR DOMAIN EXPERTISE to fill in the gaps:
                       - Add specific actors/personas (who uses this? who benefits?)
                       - Add measurable outcomes with specific targets and thresholds
                       - Add clear, testable acceptance criteria
                       - Add technology considerations and industry best practices
                       - Reference relevant standards, protocols, or compliance frameworks
                       - For GLOSSARY/TERMINOLOGY entries: transform into actionable requirements
                         about data standards, validation rules, or system configuration
                       - For RISK DESCRIPTIONS: transform into specific monitoring, alerting,
                         or control requirements that mitigate the described risk
                       - For BUSINESS VALUE statements: transform into measurable capability
                         requirements with KPIs
                       - For ARCHITECTURE descriptions: transform into specific non-functional
                         requirements with targets (latency, throughput, availability)
                    3. OUTPUT an enriched description that is detailed enough to create a
                       comprehensive, implementation-ready epic

                    ─── OUTPUT FORMAT (strictly enforced) ────────────────────────
                    For EACH requirement, output exactly ONE block:

                    ENRICHED|<req_id>|<enriched_title>|<enriched_description>

                    Where <enriched_description> is a rich paragraph (200-400 words) containing:
                    - WHO: Primary and secondary actors
                    - WHAT: Specific system behaviors and capabilities
                    - WHY: Business value and measurable outcomes
                    - HOW: Key technical approach and constraints
                    - ACCEPTANCE: 3-5 specific, testable acceptance criteria
                    - STANDARDS: Any relevant industry standards or best practices

                    Use semicolons within the enriched_description to separate sections.
                    Do NOT use pipe characters (|) inside the description.
                    Output ONLY ENRICHED lines. No markdown, no explanations.
                    """,
                UserPrompt = $"""
                    Module: {module} | Batch: {b + 1}/{batches.Count} | Requirements: {batch.Count}

                    === PROJECT DOMAIN MODEL ===
                    {domainCtx}

                    === REQUIREMENTS TO ENRICH ===
                    {string.Join("\n", reqBlocks)}

                    Enrich ALL {batch.Count} requirements above. Output {batch.Count} ENRICHED lines.
                    """,
                Temperature = 0.3,
                MaxTokens = 16384,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var cleaned = response.Content;
                if (cleaned.Contains("```"))
                {
                    cleaned = Regex.Replace(cleaned, @"```[a-zA-Z]*\s*\n?", "");
                    cleaned = cleaned.Replace("```", "");
                }

                foreach (var line in cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!line.StartsWith("ENRICHED|", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = line.Split('|', 4); // Split into max 4 parts: ENRICHED|id|title|description
                    if (parts.Length < 4) continue;

                    var reqId = parts[1].Trim();
                    var enrichedTitle = parts[2].Trim();
                    var enrichedDesc = parts[3].Trim();

                    if (!string.IsNullOrWhiteSpace(enrichedDesc))
                    {
                        enriched[reqId] = $"[Enriched Title]: {enrichedTitle}\n[Enriched Description]: {enrichedDesc}";
                    }
                }

                _logger.LogInformation("Phase 0 batch {Batch}/{Total}: enriched {Count} requirements (model={Model})",
                    b + 1, batches.Count, batch.Count(r => enriched.ContainsKey(r.Id)), response.Model);
            }
            else
            {
                _logger.LogWarning("Phase 0 batch {Batch} failed: {Error} — using original descriptions", b + 1, response.Error);
            }
        }

        return enriched;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PHASE 1: Requirements → Epics
    // Batched: ~5 requirements per LLM call. Each requirement becomes
    // exactly ONE epic with rich business context, success criteria, and
    // service mappings. Uses Phase 0 enriched content for higher quality.
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<ExpandedRequirement>> Phase1_RequirementsToEpicsAsync(
        List<Requirement> reqs,
        string module,
        int iteration,
        Dictionary<string, List<string>> reqServiceMap,
        Dictionary<string, List<string>> reqDeps,
        QualityAssessmentResult qualityInsights,
        string domainCtx,
        Dictionary<string, string> enrichedDescriptions,
        Func<AgentType, string, Task>? reportProgress,
        CancellationToken ct)
    {
        var allEpics = new List<ExpandedRequirement>();
        var batches = reqs.Chunk(Phase1BatchSize).ToList();

        for (var b = 0; b < batches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = batches[b].ToList();

            if (reportProgress is not null)
                await reportProgress(Type, $"  Phase 1 batch {b + 1}/{batches.Count}: converting {batch.Count} enriched requirements to Epics...");

            var reqSummaries = batch.Select(r =>
            {
                var services = reqServiceMap.TryGetValue(r.Id, out var slist) ? string.Join(", ", slist) : "Unknown";
                var deps = reqDeps.TryGetValue(r.Id, out var dlist) ? string.Join(", ", dlist) : "None";
                var hint = qualityInsights.QualityHints.TryGetValue(r.Id, out var h) ? h : "";
                var enrichedBlock = enrichedDescriptions.TryGetValue(r.Id, out var enrichedText)
                    ? $"\n    === ENRICHED (domain-expert analysis) ===\n    {enrichedText}"
                    : "";
                return $"""
                      REQ[{r.Id}]: {r.Title}
                        Original Description: {Truncate(r.Description, 300)}
                        Acceptance Criteria: {(r.AcceptanceCriteria.Count > 0 ? string.Join("; ", r.AcceptanceCriteria) : "None")}
                        Tags: {string.Join(", ", r.Tags)}
                        Services: {services} | Depends: {deps}
                        Quality Note: {hint}{enrichedBlock}
                    """;
            });

            var prompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a principal software architect creating EPICS from enriched
                    requirements. Each requirement has been pre-analyzed by a domain expert
                    (shown as "ENRICHED" block). Use BOTH the original requirement AND the
                    enriched analysis to create comprehensive, implementation-ready epics.

                    ─── CRITICAL INSTRUCTIONS ─────────────────────────────────────
                    • You MUST produce EXACTLY one EPIC per requirement — no more, no less
                    • USE the enriched description as your PRIMARY source — it contains
                      domain expertise, specific actors, measurable outcomes, and acceptance
                      criteria that the original requirement was missing
                    • ADD your own knowledge: industry best practices, relevant standards,
                      technology considerations, and real-world implementation patterns
                    • For weak original requirements (glossary, risk descriptions, business
                      value statements), the enriched version transforms them into actionable
                      epics — leverage that transformation fully
                    • Each epic MUST have:
                      - Clear, measurable success criteria with specific thresholds
                      - Identified actors/personas who interact with the system
                      - Concrete scope boundaries (what's in AND what's out)
                      - Realistic business value that stakeholders can validate
                      - ALL affected microservices identified

                    ─── WHAT IS AN EPIC? ──────────────────────────────────────────
                    An Epic is a large body of work that delivers significant business value.
                    It will later be broken down into User Stories and Tasks.
                    Each Epic should take 2-8 weeks of team effort to complete.

                    ─── OUTPUT FORMAT (strictly enforced) ─────────────────────────
                    Output ONLY pipe-delimited lines. NO markdown, NO explanations, NO blank lines.

                    EPIC|<id>|<title>|<summary>|<business_value>|<success_criteria_semicolon_sep>|<scope>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>

                    Field details:
                    • id: E-<MODULE>-<seq> e.g. E-BRD-001
                    • title: Concise epic name (5-12 words)
                    • summary: 2-4 sentences describing the epic's purpose, scope, and key capabilities
                    • business_value: Why this matters — with measurable impact (1-2 sentences)
                    • success_criteria: 3-5 specific measurable outcomes separated by semicolons
                    • scope: What's included AND what's explicitly excluded
                    • priority: 1=critical path, 2=important, 3=nice-to-have
                    • services_csv: Which microservices are involved
                    • depends_on_ids_csv: IDs of other epics this depends on (or empty)
                    """,
                UserPrompt = $"""
                    Module: {module} | Batch: {b + 1}/{batches.Count}

                    === DOMAIN MODEL ===
                    {domainCtx}

                    === ENRICHED REQUIREMENTS ({batch.Count}) ===
                    Each requirement below includes its original description AND a domain-expert
                    enriched analysis. Use BOTH to create comprehensive epics.

                    {string.Join("\n", reqSummaries)}

                    Generate exactly {batch.Count} EPIC lines — one per requirement above.
                    Use IDs E-{module.ToUpperInvariant().Replace(" ", "")}-{(b * Phase1BatchSize) + 1:D3} through E-{module.ToUpperInvariant().Replace(" ", "")}-{(b * Phase1BatchSize) + batch.Count:D3}.
                    Every requirement MUST become an epic, even if the original was weak.
                    The enriched analysis gives you the material to build a great epic.
                    """,
                Temperature = 0.2,
                MaxTokens = 12288,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var parsed = ParseExpansionResponse(response.Content, module, iteration);
                var epicsOnly = parsed.Where(p => p.ItemType == WorkItemType.Epic).ToList();
                _logger.LogInformation("Phase 1 batch {Batch}/{Total}: {Count} epics from LLM (model={Model})",
                    b + 1, batches.Count, epicsOnly.Count, response.Model);
                allEpics.AddRange(epicsOnly);
            }
            else
            {
                _logger.LogWarning("Phase 1 batch {Batch} failed: {Error}", b + 1, response.Error);
                // Generate minimal fallback epics for this batch
                foreach (var req in batch)
                {
                    allEpics.Add(new ExpandedRequirement
                    {
                        Id = $"E-{module.ToUpperInvariant().Replace(" ", "")}-{allEpics.Count + 1:D3}",
                        ItemType = WorkItemType.Epic,
                        Title = req.Title,
                        Summary = req.Description,
                        Description = $"Summary: {req.Description}\nBusiness Value: {string.Join("; ", req.AcceptanceCriteria)}",
                        BusinessValue = string.Join("; ", req.AcceptanceCriteria),
                        Module = module,
                        Priority = 2,
                        Iteration = iteration,
                        AffectedServices = reqServiceMap.TryGetValue(req.Id, out var sl) ? sl : [],
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                }
            }
        }

        return allEpics;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PHASE 2: Epics → User Stories + Use Cases
    // 1-3 epics per LLM call. For each epic, produce 3-5 vertical-sliced
    // user stories with INVEST criteria and 1 use case with actor flows.
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<ExpandedRequirement>> Phase2_EpicsToStoriesAsync(
        List<ExpandedRequirement> epics,
        List<Requirement> originalReqs,
        string module,
        int iteration,
        string domainCtx,
        Func<AgentType, string, Task>? reportProgress,
        CancellationToken ct)
    {
        var allResults = new List<ExpandedRequirement>();
        var batches = epics.Chunk(Phase2BatchSize).ToList();

        for (var b = 0; b < batches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = batches[b].ToList();

            if (reportProgress is not null)
                await reportProgress(Type, $"  Phase 2 batch {b + 1}/{batches.Count}: splitting {batch.Count} epics into Stories & Use Cases...");

            var epicSummaries = batch.Select(e =>
                $"  EPIC[{e.Id}]: {e.Title}\n    Summary: {Truncate(e.Summary, 300)}\n    Business Value: {Truncate(e.BusinessValue, 200)}\n    Success Criteria: {string.Join("; ", e.SuccessCriteria)}\n    Services: {string.Join(", ", e.AffectedServices)}\n    Depends On: {string.Join(", ", e.DependsOn)}");

            var prompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a senior product owner decomposing Epics into User Stories and Use Cases.
                    The project's technology stack and domain context will be provided below.

                    ─── YOUR ONLY JOB ─────────────────────────────────────────────
                    For EACH epic provided, generate:
                    • 3-5 User Stories (vertical slices delivering end-to-end user value)
                    • 1 Use Case (actor-system interaction flow)

                    ─── USER STORY RULES ──────────────────────────────────────────
                    Title format: "As a [Persona], I want to [Action] so that [Benefit]"
                    Derive personas from the domain model and requirements context.
                    Common personas include end users, administrators, operators, and system integrations.

                    INVEST criteria (mandatory):
                    • Independent: can be developed without blocking on other stories
                    • Negotiable: describes WHAT, not prescriptive HOW
                    • Valuable: delivers observable value to a real user
                    • Estimable: enough detail to size (1-5 story points)
                    • Small: completable in 1 sprint (1-5 story points)
                    • Testable: Given/When/Then acceptance criteria

                    VERTICAL SLICING (mandatory):
                    Each story must touch multiple layers (DB + Service + API + UI).
                    A story that only touches one layer is a TASK, not a story.

                    Acceptance criteria MUST use Given/When/Then format:
                    "Given [context], when [action], then [outcome]"

                    ─── USE CASE RULES ────────────────────────────────────────────
                    Each use case describes the primary actor's interaction with the system.
                    Include numbered main flow steps, alternative flows, pre/post conditions.

                    ─── OUTPUT FORMAT ─────────────────────────────────────────────
                    Output ONLY pipe-delimited lines. NO markdown, NO explanations.

                    STORY|<id>|<parent_epic_id>|<title>|<acceptance_criteria_semicolon_sep>|<story_points>|<labels_csv>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>|<detailed_spec>
                    USECASE|<id>|<parent_epic_id>|<title>|<actor>|<preconditions>|<main_flow_steps_semicolon_sep>|<alt_flows>|<postconditions>|<services_csv>

                    ID rules:
                    • Story IDs: US-<EPIC_NUM>-<seq> e.g. US-BRD-001-01
                    • Use Case IDs: UC-<EPIC_NUM>-01 e.g. UC-BRD-001-01
                    • Parent epic ID must match exactly

                    Story fields:
                    • acceptance_criteria: 3-5 Given/When/Then items separated by semicolons
                    • story_points: 1, 2, 3, 5, or 8
                    • labels: functional area tags derived from the domain (e.g. user-management, reporting)
                    • detailed_spec: 2-3 sentences about what this story entails technically
                    """,
                UserPrompt = $"""
                    Module: {module}

                    === DOMAIN MODEL ===
                    {domainCtx}

                    === EPICS TO SPLIT ({batch.Count}) ===
                    {string.Join("\n\n", epicSummaries)}

                    For each epic above, generate 3-5 User Stories + 1 Use Case.
                    Total expected: {batch.Count * 4}-{batch.Count * 6} items.
                    Ensure every story has acceptance criteria in Given/When/Then format.
                    """,
                Temperature = 0.3,
                MaxTokens = 16384,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var parsed = ParseExpansionResponse(response.Content, module, iteration);
                var storiesAndUc = parsed.Where(p =>
                    p.ItemType == WorkItemType.UserStory || p.ItemType == WorkItemType.UseCase).ToList();
                _logger.LogInformation("Phase 2 batch {Batch}/{Total}: {Stories} stories + {UC} use cases (model={Model})",
                    b + 1, batches.Count,
                    storiesAndUc.Count(s => s.ItemType == WorkItemType.UserStory),
                    storiesAndUc.Count(s => s.ItemType == WorkItemType.UseCase),
                    response.Model);
                allResults.AddRange(storiesAndUc);
            }
            else
            {
                _logger.LogWarning("Phase 2 batch {Batch} failed: {Error}", b + 1, response.Error);
            }
        }

        return allResults;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PHASE 3: User Stories → Tasks
    // 2-5 stories per LLM call. For each story, produce 5-7 implementation
    // tasks spanning the full stack: contract, DB, service, API, UI, tests.
    // Each task has a clear Definition of Done and dependency chain.
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<ExpandedRequirement>> Phase3_StoriesToTasksAsync(
        List<ExpandedRequirement> stories,
        List<ExpandedRequirement> epics,
        List<Requirement> originalReqs,
        string module,
        int iteration,
        string domainCtx,
        Func<AgentType, string, Task>? reportProgress,
        CancellationToken ct)
    {
        var allTasks = new List<ExpandedRequirement>();
        var batches = stories.Chunk(Phase3BatchSize).ToList();

        // Build epic lookup for context
        var epicLookup = epics.ToDictionary(e => e.Id, e => e, StringComparer.OrdinalIgnoreCase);

        for (var b = 0; b < batches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = batches[b].ToList();

            if (reportProgress is not null)
                await reportProgress(Type, $"  Phase 3 batch {b + 1}/{batches.Count}: decomposing {batch.Count} stories into Tasks...");

            var storySummaries = batch.Select(s =>
            {
                var parentEpic = epicLookup.TryGetValue(s.ParentId ?? "", out var ep) ? ep.Title : "Unknown Epic";
                var ac = string.Join("; ", s.AcceptanceCriteria.Take(5));
                return $"""
                      STORY[{s.Id}] (parent: {s.ParentId}, epic: {parentEpic})
                        Title: {s.Title}
                        Story Points: {s.StoryPoints}
                        Acceptance Criteria: {ac}
                        Services: {string.Join(", ", s.AffectedServices)}
                        Spec: {Truncate(s.DetailedSpec, 300)}
                    """;
            });

            var prompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a senior full-stack engineer decomposing User Stories into implementation
                    Tasks. The project's technology stack and domain context will be provided below.

                    ─── YOUR ONLY JOB ─────────────────────────────────────────────
                    For EACH User Story, generate 5-7 concrete implementation Tasks.
                    Tasks must follow a strict dependency chain within each story.

                    ─── MANDATORY TASK CATEGORIES (in dependency order) ────────────
                    1. CONTRACT — Define API contract (OpenAPI spec, request/response DTOs,
                       validation rules). Tag: contract
                       DoD: OpenAPI spec committed; DTO classes created; validation attributes defined

                    2. DATABASE — Entity model, EF Core migration, indexes, constraints, seed data.
                       Tag: database
                       DoD: Migration created and applied; entity with all fields, FKs, indexes;
                       seed data script if applicable

                    3. SERVICE — Business logic layer with validation, domain events, error handling.
                       Tag: service
                       DoD: Service class with all methods; input validation; business rules enforced;
                       domain events published; unit tests pass

                    4. API — Controller/endpoint wiring service to HTTP, auth policies, error mapping.
                       Tag: api
                       DoD: Endpoint returns correct status codes; auth policy applied; request
                       validation returns 400; integration test passes

                    5. UI — Razor Page or Blazor component: list page, detail view, forms, navigation.
                       Tag: ui
                       DoD: Page renders correctly; form validation works; navigation links added;
                       responsive layout; accessibility basics

                    6. INTEGRATION TEST — Tests the full API endpoint with real DB.
                       Tag: testing
                       DoD: Happy path test passes; validation error test; unauthorized test;
                       not-found test; all assertions verify response body

                    7. E2E TEST — Full user flow test from UI through API to database verification.
                       Tag: testing
                       DoD: Test simulates user flow; verifies database state; cleanup after test

                    ─── TASK QUALITY REQUIREMENTS ─────────────────────────────────
                    Every task MUST have:
                    • Clear title: "[T-<STORY_NUM>-<TAG>] Verb-noun action"
                    • Description: 2-3 sentences of what exactly to build
                    • Technical notes: Specific classes, methods, routes, table names
                    • Definition of Done: 3-5 checkable items separated by semicolons
                    • Effort: 2-8 hours per task (if bigger, it needs splitting later)
                    • Dependencies: Reference parent story and predecessor task IDs

                    ─── OUTPUT FORMAT ─────────────────────────────────────────────
                    Output ONLY pipe-delimited lines. NO markdown, NO explanations.

                    TASK|<id>|<parent_story_id>|<title>|<description>|<technical_notes>|<definition_of_done_semicolon_sep>|<tags_csv>|<priority 1-3>|<services_csv>|<detailed_spec>

                    ID format: T-<STORY_NUM>-<SEQ>-<TAG> e.g. T-BRD-001-01-01-CONTRACT
                    Tags: contract, database, service, api, ui, testing
                    """,
                UserPrompt = $"""
                    Module: {module}

                    === DOMAIN MODEL ===
                    {domainCtx}

                    === USER STORIES TO DECOMPOSE ({batch.Count}) ===
                    {string.Join("\n", storySummaries)}

                    Generate 5-7 Tasks per story. Total expected: {batch.Count * 5}-{batch.Count * 7} tasks.
                    Follow the dependency chain: CONTRACT → DATABASE → SERVICE → API → UI → INTEGRATION TEST → E2E TEST.
                    Every task must have a concrete Definition of Done with 3-5 checkable items.
                    """,
                Temperature = 0.2,
                MaxTokens = 32768,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var parsed = ParseExpansionResponse(response.Content, module, iteration);
                var tasksOnly = parsed.Where(p => p.ItemType == WorkItemType.Task).ToList();
                _logger.LogInformation("Phase 3 batch {Batch}/{Total}: {Count} tasks (model={Model})",
                    b + 1, batches.Count, tasksOnly.Count, response.Model);
                allTasks.AddRange(tasksOnly);
            }
            else
            {
                _logger.LogWarning("Phase 3 batch {Batch} failed: {Error}", b + 1, response.Error);
            }
        }

        return allTasks;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PHASE 4: Recursive Task Refinement
    // Asks the LLM to evaluate each task: is it atomic (2-8 hours, one
    // person, clear DoD)? If not, split it into sub-tasks. Recurse until
    // all tasks are indivisible or MaxRefinementDepth is reached.
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<ExpandedRequirement>> Phase4_RecursiveTaskRefinementAsync(
        List<ExpandedRequirement> tasks,
        string module,
        int iteration,
        string domainCtx,
        Func<AgentType, string, Task>? reportProgress,
        CancellationToken ct,
        int depth)
    {
        if (depth >= MaxRefinementDepth || tasks.Count == 0)
        {
            if (depth >= MaxRefinementDepth)
                _logger.LogInformation("Phase 4: max refinement depth {Depth} reached for module '{Module}' with {Count} tasks",
                    depth, module, tasks.Count);
            return tasks;
        }

        // Ask LLM to evaluate which tasks need splitting
        var taskSummaries = tasks.Select(t =>
            $"  TASK[{t.Id}] parent={t.ParentId}\n    Title: {t.Title}\n    Description: {Truncate(t.Description, 200)}\n    DoD: {string.Join("; ", t.DefinitionOfDone.Take(5))}\n    Tags: {string.Join(", ", t.Tags)}");

        // Batch into groups of 20 for evaluation
        var evalBatches = tasks.Chunk(20).ToList();
        var needsSplitting = new List<ExpandedRequirement>();
        var atomic = new List<ExpandedRequirement>();

        for (var b = 0; b < evalBatches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = evalBatches[b].ToList();

            var batchSummaries = batch.Select(t =>
                $"TASK[{t.Id}]: {t.Title} | DoD: {string.Join("; ", t.DefinitionOfDone.Take(3))} | Tags: {string.Join(",", t.Tags)}");

            var evalPrompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a senior engineering lead evaluating whether tasks are atomic enough
                    for a developer to pick up and complete in 2-8 hours.

                    For EACH task, output exactly one line:
                    ATOMIC|<task_id>|<reason>
                    or
                    SPLIT|<task_id>|<reason>

                    A task needs SPLIT if ANY of these are true:
                    • It would take more than 8 hours
                    • It covers multiple distinct technical concerns (e.g., "build API and UI")
                    • Its Definition of Done has items spanning different layers
                    • A single developer couldn't complete it without context-switching
                    • The description is vague or lacks specific technical details

                    A task is ATOMIC if ALL of these are true:
                    • 2-8 hours of focused work
                    • Single technical concern (one layer, one component)
                    • Clear, checkable Definition of Done
                    • One person can complete it independently
                    • Specific enough: mentions concrete classes, methods, routes, or table names

                    Output ONLY the evaluation lines. NO markdown, NO explanations.
                    """,
                UserPrompt = $"""
                    Evaluate these {batch.Count} tasks:

                    {string.Join("\n", batchSummaries)}
                    """,
                Temperature = 0.1,
                MaxTokens = 4096,
                RequestingAgent = Name
            };

            var evalResponse = await _llm.GenerateAsync(evalPrompt, ct);
            if (evalResponse.Success && !string.IsNullOrWhiteSpace(evalResponse.Content))
            {
                var splitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in evalResponse.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2 && parts[0].Trim().Equals("SPLIT", StringComparison.OrdinalIgnoreCase))
                        splitIds.Add(parts[1].Trim());
                }

                foreach (var task in batch)
                {
                    if (splitIds.Contains(task.Id))
                        needsSplitting.Add(task);
                    else
                        atomic.Add(task);
                }
            }
            else
            {
                // If evaluation fails, treat all as atomic
                atomic.AddRange(batch);
            }
        }

        if (needsSplitting.Count == 0)
        {
            _logger.LogInformation("Phase 4 depth {Depth}: all {Count} tasks are atomic", depth, tasks.Count);
            return tasks;
        }

        if (reportProgress is not null)
            await reportProgress(Type, $"  Phase 4 depth {depth + 1}: splitting {needsSplitting.Count} non-atomic tasks ({atomic.Count} already atomic)...");

        // Split the non-atomic tasks
        var splitBatches = needsSplitting.Chunk(5).ToList();
        var newSubTasks = new List<ExpandedRequirement>();

        for (var b = 0; b < splitBatches.Count; b++)
        {
            ct.ThrowIfCancellationRequested();
            var batch = splitBatches[b].ToList();

            var splitSummaries = batch.Select(t => $"""
                  TASK[{t.Id}] parent={t.ParentId}
                    Title: {t.Title}
                    Description: {Truncate(t.Description, 300)}
                    Technical Notes: {Truncate(t.TechnicalNotes, 200)}
                    DoD: {string.Join("; ", t.DefinitionOfDone)}
                    Tags: {string.Join(", ", t.Tags)}
                    Services: {string.Join(", ", t.AffectedServices)}
                """);

            var splitPrompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a senior engineer splitting oversized tasks into smaller sub-tasks.

                    ─── RULES ─────────────────────────────────────────────────────
                    For EACH task provided, split it into 2-4 smaller sub-tasks where:
                    • Each sub-task is 2-4 hours of work
                    • Each sub-task has a single technical concern
                    • Sub-tasks have clear dependency order between them
                    • Each sub-task has 3-5 concrete Definition of Done items
                    • Technical notes mention specific classes, methods, routes, tables

                    ─── OUTPUT FORMAT ─────────────────────────────────────────────
                    TASK|<id>|<parent_story_id>|<title>|<description>|<technical_notes>|<definition_of_done_semicolon_sep>|<tags_csv>|<priority 1-3>|<services_csv>|<detailed_spec>

                    ID format: <original_task_id>-<sub_seq> e.g. T-BRD-001-01-01-CONTRACT-1
                    The parent_story_id should be the SAME parent as the original task.

                    Output ONLY pipe-delimited TASK lines. NO markdown, NO explanations.
                    """,
                UserPrompt = $"""
                    Module: {module}

                    === DOMAIN MODEL ===
                    {domainCtx}

                    === TASKS TO SPLIT ({batch.Count}) ===
                    {string.Join("\n", splitSummaries)}

                    Split each task into 2-4 atomic sub-tasks. Keep the same parent_story_id.
                    """,
                Temperature = 0.2,
                MaxTokens = 16384,
                RequestingAgent = Name
            };

            var splitResponse = await _llm.GenerateAsync(splitPrompt, ct);
            if (splitResponse.Success && !string.IsNullOrWhiteSpace(splitResponse.Content))
            {
                var parsed = ParseExpansionResponse(splitResponse.Content, module, iteration);
                var subTasks = parsed.Where(p => p.ItemType == WorkItemType.Task).ToList();
                _logger.LogInformation("Phase 4 depth {Depth} batch {Batch}: split {Original} tasks → {New} sub-tasks",
                    depth, b + 1, batch.Count, subTasks.Count);
                newSubTasks.AddRange(subTasks);
            }
            else
            {
                // If split fails, keep original tasks as-is
                _logger.LogWarning("Phase 4 depth {Depth} batch {Batch} split failed, keeping originals", depth, b + 1);
                atomic.AddRange(batch);
            }
        }

        // Recurse on the new sub-tasks to check if they're truly atomic
        if (newSubTasks.Count > 0)
        {
            var refinedSubTasks = await Phase4_RecursiveTaskRefinementAsync(
                newSubTasks, module, iteration, domainCtx, reportProgress, ct, depth + 1);
            atomic.AddRange(refinedSubTasks);
        }

        _logger.LogInformation("Phase 4 depth {Depth} complete: {Atomic} atomic tasks total for module '{Module}'",
            depth, atomic.Count, module);
        return atomic;
    }

    // ─── Requirement→Service Mapping ───────────────────────────────────
    private static Dictionary<string, List<string>> BuildRequirementServiceMap(
        List<Requirement> requirements, ParsedDomainModel? domainModel, AgentContext context)
    {
        var map = new Dictionary<string, List<string>>();

        foreach (var req in requirements)
        {
            var services = new List<string>();
            var lower = $"{req.Title} {req.Description}".ToLowerInvariant();

            foreach (var svc in ServiceCatalogResolver.GetServices(context))
            {
                var svcLower = svc.Name.Replace("Service", "").ToLowerInvariant();
                if (req.Tags.Any(t => t.Equals(svc.ShortName, StringComparison.OrdinalIgnoreCase)) ||
                    lower.Contains(svcLower) ||
                    svc.Entities.Any(e => lower.Contains(e.ToLowerInvariant())))
                {
                    services.Add(svc.Name);
                }
            }

            // Fallback: match by module keywords
            if (services.Count == 0)
            {
                var svc = InferServiceFromModule(req.Module);
                if (svc is not null) services.Add(svc);
            }

            map[req.Id] = services.Distinct().ToList();
        }

        return map;
    }

    // ─── Requirement Dependency Graph ──────────────────────────────────
    private static Dictionary<string, List<string>> BuildRequirementDependencies(
        List<Requirement> requirements, Dictionary<string, List<string>> serviceMap, AgentContext context)
    {
        var deps = new Dictionary<string, List<string>>();

        foreach (var req in requirements)
        {
            var depIds = new List<string>();

            // Explicit DependsOn from the requirement parsing (these are intentional)
            depIds.AddRange(req.DependsOn);

            // Implicit dependencies from service catalog — only add where there's
            // a clear directional dependency (service A depends on service B) AND
            // the dependency isn't mutual (preventing circular blocking).
            if (serviceMap.TryGetValue(req.Id, out var services))
            {
                foreach (var svcName in services)
                {
                    var svc = ServiceCatalogResolver.ByName(context, svcName);
                    if (svc is null) continue;

                    foreach (var depSvc in svc.DependsOn)
                    {
                        var depReqs = requirements
                            .Where(r => r.Id != req.Id &&
                                        serviceMap.TryGetValue(r.Id, out var rsvcs) &&
                                        rsvcs.Any(s => s.Equals(depSvc, StringComparison.OrdinalIgnoreCase)))
                            .Select(r => r.Id);
                        depIds.AddRange(depReqs);
                    }
                }
            }

            deps[req.Id] = depIds.Distinct().ToList();
        }

        // Break mutual/circular dependencies — only keep the edge from the
        // lower-ID requirement to the higher-ID one (deterministic ordering).
        foreach (var (reqId, depList) in deps)
        {
            depList.RemoveAll(depId =>
                deps.TryGetValue(depId, out var reverseList) &&
                reverseList.Contains(reqId));
        }

        return deps;
    }

    // ─── Resolve dependencies across expanded items ────────────────────
    private static void ResolveExpandedDependencies(List<ExpandedRequirement> items, ParsedDomainModel? domainModel, AgentContext context)
    {
        // Use first-wins to handle any residual duplicates
        var byId = new Dictionary<string, ExpandedRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            byId.TryAdd(item.Id, item);

        foreach (var item in items)
        {
            // Auto-dependencies: within the SAME story — DB → Service → API → Test ordering
            // Only add if the item doesn't already have the correct sequential deps set
            // (CreateFallbackItems already sets CONTRACT → DB → SVC → API → ITEST → E2E)
            // Skip "contract" items — they are chain heads with no upstream deps.
            if (item.ItemType == WorkItemType.Task && item.DependsOn.Count == 0
                && !item.Tags.Contains("contract"))
            {
                if (item.Tags.Contains("service") || item.Tags.Contains("api"))
                {
                    var dbSibling = items.FirstOrDefault(i =>
                        i.ParentId == item.ParentId && i.Tags.Contains("database"));
                    if (dbSibling is not null && !item.DependsOn.Contains(dbSibling.Id))
                        item.DependsOn.Add(dbSibling.Id);
                }
                if (item.Tags.Contains("testing"))
                {
                    var svcSibling = items.FirstOrDefault(i =>
                        i.ParentId == item.ParentId && i.Tags.Contains("service"));
                    if (svcSibling is not null && !item.DependsOn.Contains(svcSibling.Id))
                        item.DependsOn.Add(svcSibling.Id);
                }
            }

            // Cross-service dependencies: apply ONLY at Epic level to avoid
            // creating O(n^2) task-to-task dependency webs that deadlock the pipeline.
            if (item.ItemType == WorkItemType.Epic)
            {
                foreach (var svcName in item.AffectedServices)
                {
                    var svc = ServiceCatalogResolver.ByName(context, svcName);
                    if (svc is null) continue;

                    foreach (var dep in svc.DependsOn)
                    {
                        var depEpic = items.FirstOrDefault(i =>
                            i.Id != item.Id &&
                            i.ItemType == WorkItemType.Epic &&
                            i.AffectedServices.Any(s => s.Equals(dep, StringComparison.OrdinalIgnoreCase)));
                        if (depEpic is not null && !item.DependsOn.Contains(depEpic.Id))
                            item.DependsOn.Add(depEpic.Id);
                    }
                }
            }

            // Build resolved chain (flattened transitive deps) — capped depth
            var chain = new List<string>();
            ResolveChain(item, byId, chain, 0);
            item.ResolvedDependencyChain = chain;
        }
    }

    private static void ResolveChain(ExpandedRequirement item,
        Dictionary<string, ExpandedRequirement> byId, List<string> chain, int depth)
    {
        if (depth > 10) return;
        foreach (var depId in item.DependsOn)
        {
            if (chain.Contains(depId)) continue;
            chain.Add(depId);
            if (byId.TryGetValue(depId, out var dep))
                ResolveChain(dep, byId, chain, depth + 1);
        }
    }

    // ─── Domain Context Builder ────────────────────────────────────────
    private static string BuildDomainContextForModule(string module, ParsedDomainModel? model)
    {
        if (model is null) return "(Domain model not yet built)";

        var sb = new StringBuilder();
        var relevantEntities = model.Entities
            .Where(e => e.FeatureTags.Any(t => t.Contains(module, StringComparison.OrdinalIgnoreCase)) ||
                        e.ServiceName.Contains(module.Replace("Service", ""), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relevantEntities.Count == 0)
            relevantEntities = model.Entities; // include all if no module match

        foreach (var entity in relevantEntities.Take(15))
        {
            sb.AppendLine($"  Entity: {entity.Name} (Service: {entity.ServiceName}, Schema: {entity.Schema})");
            foreach (var f in entity.Fields.Take(15))
                sb.AppendLine($"    - {f.Name}: {f.Type}{(f.IsKey ? " [PK]" : "")}{(f.IsRequired ? " [Required]" : "")}{(f.IsAuditField ? " [Audit]" : "")}");
            sb.AppendLine();
        }

        // Relevant endpoints
        var endpoints = model.ApiEndpoints
            .Where(e => relevantEntities.Any(re => re.Name == e.EntityName))
            .Take(20);
        foreach (var ep in endpoints)
            sb.AppendLine($"  API: {ep.Method} {ep.Path} → {ep.OperationName} ({ep.ServiceName})");

        // Relevant NFRs
        foreach (var nfr in model.NfrRequirements)
            sb.AppendLine($"  NFR: {nfr.Id} ({nfr.Category}): {nfr.Description}");

        return sb.ToString();
    }

    // ─── Response Parsing ──────────────────────────────────────────────
    private static List<ExpandedRequirement> ParseExpansionResponse(string content, string module, int iteration)
    {
        var items = new List<ExpandedRequirement>();

        // Strip markdown code fences that Gemini often wraps around output
        var cleaned = content;
        if (cleaned.Contains("```"))
        {
            cleaned = Regex.Replace(cleaned, @"```[a-zA-Z]*\s*\n?", "");
            cleaned = cleaned.Replace("```", "");
        }

        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;

            var kind = parts[0].Trim().ToUpperInvariant();
            switch (kind)
            {
                case "EPIC" when parts.Length >= 10:
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ItemType = WorkItemType.Epic,
                        Title = parts[2].Trim(),
                        Summary = parts[3].Trim(),
                        BusinessValue = parts[4].Trim(),
                        SuccessCriteria = ParseSemicolonList(parts[5]),
                        Scope = parts[6].Trim(),
                        Description = $"Summary: {parts[3].Trim()}\nBusiness Value: {parts[4].Trim()}\nScope: {parts[6].Trim()}",
                        Module = module,
                        Priority = ParsePriority(parts[7]),
                        Iteration = iteration,
                        AffectedServices = parts.Length > 8
                            ? [.. parts[8].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DependsOn = parts.Length > 9
                            ? [.. parts[9].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;

                case "STORY" when parts.Length >= 11:
                    var rawStoryTitle = parts[3].Trim();
                    var normalizedStoryTitle = EnsureUserStoryTemplate(rawStoryTitle, parts[10].Trim());
                    var normalizedCriteria = EnsureGivenWhenThenCriteria(ParseSemicolonList(parts[4]));
                    var normalizedStoryPoints = ParseStoryPoints(parts[5]);
                    var normalizedLabels = ParseLabels(parts[6]);
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.UserStory,
                        Title = normalizedStoryTitle,
                        Description = parts[10].Trim(),
                        Module = module,
                        Priority = ParsePriority(parts[7]),
                        Iteration = iteration,
                        AcceptanceCriteria = normalizedCriteria,
                        StoryPoints = normalizedStoryPoints,
                        Labels = normalizedLabels,
                        AffectedServices = parts.Length > 8
                            ? [.. parts[8].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DependsOn = parts.Length > 9
                            ? [.. parts[9].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DetailedSpec = parts[10].Trim(),
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;

                case "USECASE" when parts.Length >= 10:
                    var normalizedUseCaseTitle = EnsureUseCaseTitle(parts[3].Trim(), module);
                    var normalizedActor = EnsureActor(parts[4].Trim());
                    var normalizedPreconditions = EnsurePreconditions(parts[5].Trim());
                    var normalizedMainFlow = EnsureMainFlow(ParseSemicolonList(parts[6]), normalizedUseCaseTitle);
                    var normalizedPostconditions = EnsurePostconditions(parts[8].Trim());
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.UseCase,
                        Title = normalizedUseCaseTitle,
                        Actor = normalizedActor,
                        Preconditions = normalizedPreconditions,
                        MainFlow = normalizedMainFlow,
                        AlternativeFlows = parts[7].Trim(),
                        Postconditions = normalizedPostconditions,
                        Description = $"Actor: {normalizedActor}\nPreconditions: {normalizedPreconditions}\nMain Flow: {string.Join("; ", normalizedMainFlow)}\nAlternative Flows: {parts[7].Trim()}\nPostconditions: {normalizedPostconditions}",
                        Module = module,
                        Priority = 2,
                        Iteration = iteration,
                        AffectedServices = parts.Length > 9
                            ? [.. parts[9].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;

                case "TASK" when parts.Length >= 11:
                    var rawTaskTags = parts[7].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    var normalizedTaskTags = EnsureTaskTags(rawTaskTags, parts[3]);
                    var normalizedTaskTitle = EnsureTaskTitle(parts[3].Trim(), parts[1].Trim());
                    var normalizedTaskDescription = EnsureTaskDescription(parts[4].Trim(), normalizedTaskTitle);
                    var normalizedTechnicalNotes = EnsureTechnicalNotes(parts[5].Trim(), normalizedTaskTags);
                    var normalizedDod = EnsureTaskDefinitionOfDone(ParseSemicolonList(parts[6]));
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.Task,
                        Title = normalizedTaskTitle,
                        Description = normalizedTaskDescription,
                        Module = module,
                        Priority = ParsePriority(parts[8]),
                        Iteration = iteration,
                        TechnicalNotes = normalizedTechnicalNotes,
                        DefinitionOfDone = normalizedDod,
                        Tags = [.. normalizedTaskTags],
                        AffectedServices = parts.Length > 9
                            ? [.. parts[9].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DetailedSpec = parts.Length > 10 ? parts[10].Trim() : "",
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;

                case "BUG" when parts.Length >= 10:
                    var normalizedBugTitle = EnsureBugTitle(parts[3].Trim());
                    var normalizedBugSeverity = EnsureBugSeverity(parts[4].Trim());
                    var normalizedBugEnvironment = EnsureBugEnvironment(parts[5].Trim());
                    var normalizedBugSteps = EnsureBugSteps(ParseSemicolonList(parts[6]));
                    var normalizedExpected = EnsureBugExpected(parts[7].Trim());
                    var normalizedActual = EnsureBugActual(parts[8].Trim());
                    var normalizedAttachments = EnsureBugAttachments(string.Empty);
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.Bug,
                        Title = normalizedBugTitle,
                        Severity = normalizedBugSeverity,
                        Environment = normalizedBugEnvironment,
                        StepsToReproduce = normalizedBugSteps,
                        ExpectedResult = normalizedExpected,
                        ActualResult = normalizedActual,
                        Description = $"Severity: {normalizedBugSeverity}\nEnvironment: {normalizedBugEnvironment}\nSteps to Reproduce:\n{string.Join("\n", normalizedBugSteps)}\nExpected Result: {normalizedExpected}\nActual Result: {normalizedActual}\nAttachments: {normalizedAttachments}",
                        Module = module,
                        Priority = 1,
                        Iteration = iteration,
                        Tags = ["bugfix"],
                        AffectedServices = parts.Length > 9
                            ? [.. parts[9].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;
            }
        }

        return items;
    }

    private static List<ExpandedRequirement> CreateFallbackItems(List<Requirement> reqs,
        string module, int iteration,
        Dictionary<string, List<string>> serviceMap,
        Dictionary<string, List<string>> depMap,
        AgentContext context)
    {
        var items = new List<ExpandedRequirement>();
        var seq = 0;

        foreach (var req in reqs)
        {
            seq++;

            // ── Resolve concrete service + entity context ──
            var services = serviceMap.TryGetValue(req.Id, out var sl) ? sl : [];
            var deps = depMap.TryGetValue(req.Id, out var dl) ? dl : [];
            var reqTitleLower = req.Title.ToLowerInvariant();

            // If module is "General", try to infer a proper module from requirement content
            var resolvedModule = module;
            if (string.Equals(resolvedModule, "General", StringComparison.OrdinalIgnoreCase))
            {
                var inferredSvc = InferServiceFromContent(reqTitleLower, req.Description.ToLowerInvariant(), context);
                if (inferredSvc is not null)
                {
                    resolvedModule = inferredSvc.Replace("Service", "");
                    if (services.Count == 0) services = [inferredSvc];
                }
            }

            // Resolve the primary MicroserviceDefinition for rich context
            var primarySvcDef = services.Count > 0
                ? ServiceCatalogResolver.ByName(context, services[0])
                : null;
            var svcName = primarySvcDef?.Name ?? (services.Count > 0 ? services[0] : "UnknownService");
            var svcShort = primarySvcDef?.ShortName ?? resolvedModule.ToLowerInvariant();
            var schema = primarySvcDef?.Schema ?? $"cl_{svcShort}";
            var entities = primarySvcDef?.Entities ?? [];
            var entityCsv = entities.Length > 0 ? string.Join(", ", entities) : req.Title;
            var primaryEntity = entities.Length > 0 ? entities[0] : SanitizeEntityName(req.Title);
            var svcNamespace = primarySvcDef?.Namespace ?? $"GNex.{resolvedModule}Service";
            var svcDeps = primarySvcDef?.DependsOn ?? [];
            var persona = InferPrimaryPersona(reqTitleLower, resolvedModule);

            var epicId = $"EPIC-{resolvedModule}-{seq:D3}";

            // ── Epic ──
            items.Add(new ExpandedRequirement
            {
                Id = epicId, ItemType = WorkItemType.Epic,
                SourceRequirementId = req.Id, Title = req.Title,
                Description = $"[{svcName}] {req.Description}",
                Module = resolvedModule,
                Priority = 2, Iteration = iteration,
                Summary = $"Implement {req.Title} in {svcName} (schema: {schema}, entities: {entityCsv}). {req.Description}",
                BusinessValue = $"Enables {persona} workflows for {req.Title} within the {svcName} bounded context.",
                SuccessCriteria = req.AcceptanceCriteria.Count > 0
                    ? [.. req.AcceptanceCriteria.Take(5)]
                    : [$"{primaryEntity} CRUD operations functional in {svcName}", "All acceptance criteria verified", "Audit trail entries created for every mutation", "Integration with dependent services ({string.Join(", ", svcDeps)}) verified"],
                Scope = $"Scope: {svcName} ({schema}) — entities: {entityCsv}. Includes API endpoints, service layer, DB migrations, and tests.",
                AffectedServices = [.. services],
                DependsOn = [.. deps],
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            // ── Single User Story (primary persona) ──
            var storyId = $"US-{resolvedModule}-{seq:D3}-01";

            items.Add(new ExpandedRequirement
            {
                Id = storyId, ParentId = epicId,
                ItemType = WorkItemType.UserStory,
                SourceRequirementId = req.Id,
                Title = $"As a {persona}, I want to manage {primaryEntity} records in {svcName} so that {req.Title} is handled correctly",
                AcceptanceCriteria =
                [
                    $"Given the {persona} is authenticated with {svcShort}-access role, when they create a {primaryEntity}, then the record is persisted in {schema} schema and an AuditEvent is emitted",
                    $"Given a {primaryEntity} exists, when the {persona} updates it, then validation rules are enforced and the UpdatedAt timestamp is refreshed",
                    $"Given the {persona} requests a list, when they call GET /api/{svcShort}/{primaryEntity.ToLowerInvariant()}s, then paginated results filtered by TenantId are returned within 2 seconds"
                ],
                StoryPoints = 5,
                Labels = ["API", "Database", "Service", "UI"],
                Description = $"Full CRUD lifecycle for {primaryEntity} in {svcName} ({schema}). Entities: {entityCsv}. Depends on: {(svcDeps.Length > 0 ? string.Join(", ", svcDeps) : "none")}.",
                Module = resolvedModule, Priority = 2, Iteration = iteration,
                AffectedServices = [.. services],
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            // ── 5 Tasks per story (vertical-slice decomposition: DB → SVC → API → UI → TEST) ──
            var dbTaskId = $"TASK-{resolvedModule}-{seq:D3}-01-DB";
            var svcTaskId = $"TASK-{resolvedModule}-{seq:D3}-01-SVC";
            var apiTaskId = $"TASK-{resolvedModule}-{seq:D3}-01-API";
            var uiTaskId = $"TASK-{resolvedModule}-{seq:D3}-01-UI";
            var testTaskId = $"TASK-{resolvedModule}-{seq:D3}-01-TEST";

            items.Add(new ExpandedRequirement
            {
                Id = dbTaskId, ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"[{dbTaskId}] Create {primaryEntity} entity and migration in {svcName}",
                Description = $"Create EF Core entity '{primaryEntity}' in {svcNamespace}.Entities, configure in {primarySvcDef?.DbContextName ?? "DbContext"}, add migration to schema '{schema}'.",
                Module = resolvedModule, Priority = 2, Iteration = iteration,
                Tags = ["database"],
                AffectedServices = [.. services],
                TechnicalNotes = $"Target schema: {schema}. Entity: {primaryEntity}. Fields: Id (text PK), TenantId (text FK, indexed), CreatedAt (timestamptz), UpdatedAt (timestamptz), IsActive (bool, default true), plus domain-specific fields from requirement. Create indexes on TenantId and primary lookup columns. Use PostgreSQL-friendly types.",
                DefinitionOfDone = [$"Migration creates {primaryEntity} table in {schema}", "Indexes on TenantId and lookup columns verified", "Seed data present for dev/test", "Rollback migration tested"],
                DetailedSpec = $"Namespace: {svcNamespace}.Entities. Entity class: {primaryEntity} with Id, TenantId, CreatedAt, UpdatedAt, IsActive. DbContext: {primarySvcDef?.DbContextName ?? resolvedModule + "DbContext"} — add DbSet<{primaryEntity}>. Configure entity in OnModelCreating with schema \"{schema}\". Add index on TenantId. {(svcDeps.Length > 0 ? $"FK relationships to: {string.Join(", ", svcDeps)}." : "")}",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            items.Add(new ExpandedRequirement
            {
                Id = svcTaskId, ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"[{svcTaskId}] Implement I{primaryEntity}Service and {primaryEntity}Service in {svcName}",
                Description = $"Create I{primaryEntity}Service interface and {primaryEntity}Service implementation in {svcNamespace}.Services with CRUD operations, FluentValidation, domain events, and TenantId filtering.",
                Module = resolvedModule, Priority = 2, Iteration = iteration,
                Tags = ["service"],
                AffectedServices = [.. services],
                DependsOn = [dbTaskId],
                TechnicalNotes = $"Inject IRepository<{primaryEntity}>. Validator class: {primaryEntity}Validator (FluentValidation). Emit {primaryEntity}Created/{primaryEntity}Updated domain events via MediatR. All queries filtered by TenantId. Service logs every mutation to AuditService.",
                DefinitionOfDone = [$"I{primaryEntity}Service interface defined", $"{primaryEntity}Service implements all CRUD methods", $"{primaryEntity}Validator enforces required fields", "Domain events emitted on Create/Update/Delete", "Unit tests >80% coverage"],
                DetailedSpec = $"Interface: I{primaryEntity}Service in {svcNamespace}.Services. Methods: GetByIdAsync(string id, string tenantId), ListAsync(string tenantId, int page, int size), CreateAsync({primaryEntity}CreateDto dto, string tenantId), UpdateAsync(string id, {primaryEntity}UpdateDto dto, string tenantId), SoftDeleteAsync(string id, string tenantId). Each method: validate input → execute → emit domain event → log audit entry.",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            var routeBase = $"api/{svcShort}/{primaryEntity.ToLowerInvariant()}s";
            items.Add(new ExpandedRequirement
            {
                Id = apiTaskId, ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"[{apiTaskId}] Build {primaryEntity}Controller and DTOs in {svcName}",
                Description = $"Create {primaryEntity}Controller at /{routeBase} in {svcNamespace}.Controllers with {primaryEntity}CreateDto, {primaryEntity}UpdateDto, {primaryEntity}ResponseDto. Wire to I{primaryEntity}Service.",
                Module = resolvedModule, Priority = 2, Iteration = iteration,
                Tags = ["api", "contract"],
                AffectedServices = [.. services],
                DependsOn = [svcTaskId],
                TechnicalNotes = $"Controller route: [{routeBase}]. Inject I{primaryEntity}Service. Use [Authorize(Policy = \"{svcShort}-access\")]. Map service exceptions to ProblemDetails. Add [ProducesResponseType] for Swagger. X-Tenant-Id header → TenantId parameter.",
                DefinitionOfDone = [$"GET /{routeBase} returns paginated list", $"GET /{routeBase}/{{id}} returns single record", $"POST /{routeBase} creates with 201 + Location header", $"PUT /{routeBase}/{{id}} updates with 200", $"DELETE /{routeBase}/{{id}} soft-deletes with 204", "Swagger documentation complete"],
                DetailedSpec = $"DTOs in {svcNamespace}.Contracts: {primaryEntity}CreateDto (required fields + validation attributes), {primaryEntity}UpdateDto (partial update), {primaryEntity}ResponseDto (all fields + links). Controller: {primaryEntity}Controller [ApiController][Route(\"{routeBase}\")]. Endpoints: GET (list, paginated, filterable), GET/{{id}}, POST, PUT/{{id}}, DELETE/{{id}}. Status codes: 200, 201, 204, 400, 401, 403, 404, 422.",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            items.Add(new ExpandedRequirement
            {
                Id = uiTaskId, ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"[{uiTaskId}] Build {primaryEntity} UI pages (list, detail, form) in {svcName}",
                Description = $"Create UI pages for {primaryEntity} management: Index (paginated list with search/filter), Detail (read-only view), Create/Edit (form with validation). Call {svcName} API at /{routeBase}. Use shared UI components and project theme.",
                Module = resolvedModule, Priority = 2, Iteration = iteration,
                Tags = ["ui"],
                AffectedServices = [.. services],
                DependsOn = [apiTaskId],
                TechnicalNotes = $"Pages in src/GNex.Web/Pages/{resolvedModule}/{primaryEntity}/. Index.cshtml (list with DataTable component, search, filter by status), Details.cshtml (read-only summary), Create.cshtml and Edit.cshtml (forms with FluentValidation client-side mirrors). Use HttpClient to call {svcName} API at /{routeBase}. Bootstrap 5.3 responsive layout. WCAG 2.1 AA accessible (ARIA labels, keyboard nav, focus management). Include breadcrumb navigation.",
                DefinitionOfDone = [$"{primaryEntity} list page renders with pagination and search", $"{primaryEntity} create form submits and shows success toast", $"{primaryEntity} edit form loads existing data and saves changes", "Responsive layout works on mobile and desktop", "WCAG 2.1 AA: all form fields have labels, ARIA attributes present", "Navigation menu includes {primaryEntity} link"],
                DetailedSpec = $"Pages: Pages/{resolvedModule}/{primaryEntity}/Index.cshtml (list), Pages/{resolvedModule}/{primaryEntity}/Details.cshtml (view), Pages/{resolvedModule}/{primaryEntity}/Create.cshtml (form), Pages/{resolvedModule}/{primaryEntity}/Edit.cshtml (form). PageModel classes inject HttpClient configured for {svcName} base URL. Use IHttpClientFactory with named client '{svcShort}-api'. DTOs: reuse {primaryEntity}ResponseDto, {primaryEntity}CreateDto, {primaryEntity}UpdateDto from API contracts. Layout: _Layout.cshtml with sidebar nav.",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            items.Add(new ExpandedRequirement
            {
                Id = testTaskId, ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"[{testTaskId}] Write tests for {primaryEntity} in {svcName}",
                Description = $"Create unit tests for {primaryEntity}Service and {primaryEntity}Validator, plus integration tests for {primaryEntity}Controller using WebApplicationFactory against {schema} schema.",
                Module = resolvedModule, Priority = 3, Iteration = iteration,
                Tags = ["testing"],
                AffectedServices = [.. services],
                DependsOn = [uiTaskId],
                TechnicalNotes = $"Unit tests: test {primaryEntity}Service.CreateAsync with valid/invalid data, test {primaryEntity}Validator rules, test TenantId isolation. Integration tests: use WebApplicationFactory<Program>, test against real PostgreSQL (TestContainers) with schema {schema}. Verify audit events emitted.",
                DefinitionOfDone = [$"Create {primaryEntity} happy-path tested", $"Update {primaryEntity} with validation errors tested", "Unauthorized access (401/403) tested", "Not found (404) tested", "TenantId isolation verified", "Audit trail entry exists after mutation"],
                DetailedSpec = $"Test project: {svcNamespace}.Tests. Test classes: {primaryEntity}ServiceTests (unit), {primaryEntity}ValidatorTests (unit), {primaryEntity}ControllerTests (integration). Scenarios: create+read round-trip, duplicate prevention, invalid input → 422, unauthorized → 401, forbidden → 403, not found → 404, tenant isolation (user A cannot see user B data), audit event logged after POST/PUT/DELETE.",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });
        }

        return items;
    }

    /// <summary>Infer a service name from requirement title and description content.</summary>
    private static string? InferServiceFromContent(string titleLower, string descLower, AgentContext context)
    {
        var combined = $"{titleLower} {descLower}";
        foreach (var svc in ServiceCatalogResolver.GetServices(context))
        {
            var svcLower = svc.Name.Replace("Service", "").ToLowerInvariant();
            if (combined.Contains(svcLower) ||
                svc.Entities.Any(e => combined.Contains(e.ToLowerInvariant())))
                return svc.Name;
        }
        return InferServiceFromModule(combined);
    }

    /// <summary>Derive a PascalCase entity name from a requirement title.</summary>
    private static string SanitizeEntityName(string title)
    {
        // Take the first meaningful words (up to 3) and PascalCase them
        var words = Regex.Replace(title, @"[^a-zA-Z0-9\s]", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !s_stopWords.Contains(w.ToLowerInvariant()))
            .Take(3)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant())
            .ToArray();
        return words.Length > 0 ? string.Join("", words) : "DomainRecord";
    }

    private static readonly HashSet<string> s_stopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "from", "with", "into", "that", "this", "are", "was",
        "will", "can", "has", "have", "should", "must", "all", "any", "each", "new",
        "key", "core", "main", "execute", "process", "workflow", "system", "define"
    };

    /// <summary>Infer primary persona based on requirement content and module context.</summary>
    private static string InferPrimaryPersona(string reqTitleLower, string module)
    {
        var moduleLower = module.ToLowerInvariant();
        if (moduleLower.Contains("billing") || moduleLower.Contains("revenue") || moduleLower.Contains("claim"))
            return "billing clerk";
        if (moduleLower.Contains("admin") || moduleLower.Contains("config") || moduleLower.Contains("permission"))
            return "administrator";
        if (moduleLower.Contains("audit") || moduleLower.Contains("compliance"))
            return "compliance officer";
        if (reqTitleLower.Contains("diagnosis") || reqTitleLower.Contains("prescription") || reqTitleLower.Contains("treatment"))
            return "domain expert";
        if (reqTitleLower.Contains("admin") || reqTitleLower.Contains("config") || reqTitleLower.Contains("permission"))
            return "administrator";
        if (reqTitleLower.Contains("billing") || reqTitleLower.Contains("claim") || reqTitleLower.Contains("invoice"))
            return "billing clerk";
        return "user";
    }

    /// <summary>
    /// LLM-powered enrichment: sends work items + microservice catalog to the LLM to resolve
    /// which service, schema, entity, and namespace each item belongs to. Falls back to the
    /// deterministic <see cref="EnrichItemsWithServiceContext"/> when the LLM call fails.
    /// </summary>
    private async Task EnrichItemsWithLlmAsync(
        List<ExpandedRequirement> items,
        List<Requirement> reqs,
        string module,
        Dictionary<string, List<string>> reqServiceMap,
        AgentContext context,
        CancellationToken ct)
    {
        if (items.Count == 0) return;

        // Build a compact catalog summary for the prompt
        var catalogLines = ServiceCatalogResolver.GetServices(context).Select(s =>
            $"- {s.Name} (schema: {s.Schema}, entities: [{string.Join(", ", s.Entities)}], " +
            $"ns: {s.Namespace}, port: {s.ApiPort}, depends: [{string.Join(", ", s.DependsOn)}])");
        var catalogBlock = string.Join("\n", catalogLines);

        // Build compact item summaries: ID | Type | Title | Module | Description (first 120 chars)
        var itemLines = items.Select(i =>
        {
            var desc = (i.Description ?? "").Length > 120 ? (i.Description ?? "")[..120] : (i.Description ?? "");
            return $"{i.Id}|{i.ItemType}|{i.Title}|{i.Module}|{desc}";
        });
        var itemBlock = string.Join("\n", itemLines);

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a software architect mapping work items to concrete microservices.

                Given a list of work items and a microservice catalog, determine which service(s)
                each item belongs to. For EACH item, output a single pipe-delimited line:

                <item_id>|<service_name>|<primary_entity>|<enrichment_note>

                Rules:
                - service_name: MUST be one of the service names from the catalog
                - primary_entity: The main entity this item works with (from the service's entity list)
                - enrichment_note: One sentence describing what this item does in context of the service
                - If an item touches multiple services, pick the PRIMARY one
                - If you cannot determine the service, use "UNKNOWN"

                Output ONLY pipe-delimited lines. NO markdown, NO explanations, NO blank lines.
                """,
            UserPrompt = $"""
                === MICROSERVICE CATALOG ===
                {catalogBlock}

                === MODULE: {module} ===

                === WORK ITEMS ({items.Count} items) ===
                Format: ID|Type|Title|Module|Description
                {itemBlock}

                Map each item to its target microservice and primary entity.
                """,
            Temperature = 0.1,
            MaxTokens = 4096,
            RequestingAgent = Name
        };

        var response = await _llm.GenerateAsync(prompt, ct);
        if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
        {
            _logger.LogWarning("LLM enrichment failed for {Module}, using deterministic fallback: {Error}",
                module, response.Error ?? "empty response");
            EnrichItemsWithServiceContext(items, reqs, module, reqServiceMap, context);
            return;
        }

        // Parse LLM mapping and build a lookup: itemId → (serviceName, primaryEntity, note)
        var itemLookup = new Dictionary<string, ExpandedRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            itemLookup.TryAdd(item.Id, item);
        var mapped = 0;

        foreach (var line in response.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 3) continue;

            var itemId = parts[0].Trim();
            var svcName = parts[1].Trim();
            var entity = parts[2].Trim();
            var note = parts.Length > 3 ? parts[3].Trim() : "";

            if (!itemLookup.TryGetValue(itemId, out var item)) continue;
            if (string.Equals(svcName, "UNKNOWN", StringComparison.OrdinalIgnoreCase)) continue;

            var svcDef = ServiceCatalogResolver.ByName(context, svcName);
            if (svcDef is null) continue;

            mapped++;

            // Apply service to AffectedServices
            if (!item.AffectedServices.Contains(svcDef.Name, StringComparer.OrdinalIgnoreCase))
                item.AffectedServices.Add(svcDef.Name);

            // Fix module from "General"
            if (string.Equals(item.Module, "General", StringComparison.OrdinalIgnoreCase))
                item.Module = svcDef.Name.Replace("Service", "");

            var schema = svcDef.Schema;
            var entityCsv = string.Join(", ", svcDef.Entities);
            var ns = svcDef.Namespace;
            var svcShort = svcDef.ShortName;
            var depsCsv = svcDef.DependsOn.Length > 0 ? string.Join(", ", svcDef.DependsOn) : "none";
            var primaryEntity = !string.IsNullOrWhiteSpace(entity) && svcDef.Entities.Contains(entity)
                ? entity
                : (svcDef.Entities.Length > 0 ? svcDef.Entities[0] : SanitizeEntityName(item.Title));

            // Stamp fields based on item type
            switch (item.ItemType)
            {
                case WorkItemType.Epic:
                    if (!item.Description.Contains(svcDef.Name))
                        item.Description = $"[{svcDef.Name}] {item.Description}";
                    item.Summary = $"Implement in {svcDef.Name} (schema: {schema}, entities: {entityCsv}). {note}".Trim();
                    item.Scope = $"Scope: {svcDef.Name} ({schema}) — entities: {entityCsv}.";
                    break;

                case WorkItemType.UserStory:
                    if (!item.Description.Contains(svcDef.Name))
                        item.Description = $"[{svcDef.Name} | {entityCsv}] {item.Description}";
                    item.DetailedSpec = $"Service: {svcDef.Name}, Schema: {schema}, Entities: {entityCsv}, Namespace: {ns}, Dependencies: {depsCsv}. {note}".Trim();
                    break;

                case WorkItemType.UseCase:
                    if (!item.Description.Contains(svcDef.Name))
                        item.Description = $"[{svcDef.Name} | {primaryEntity}] {item.Description}";
                    item.Postconditions = $"Data persisted in {schema} schema via {svcDef.Name}. {note}".Trim();
                    break;

                case WorkItemType.Task:
                    if (!item.Description.Contains(svcDef.Name))
                        item.Description = $"[{svcDef.Name} | {schema}] {item.Description}";
                    item.TechnicalNotes = $"Service: {svcDef.Name}, Schema: {schema}, Entities: {entityCsv}, Namespace: {ns}, Port: {svcDef.ApiPort}, Dependencies: {depsCsv}. {note}".Trim();
                    item.DetailedSpec = $"Target: {svcDef.Name} ({ns}), Schema: {schema}, Primary Entity: {primaryEntity}, Route: api/{svcShort}/{primaryEntity.ToLowerInvariant()}s. {note}".Trim();
                    break;

                case WorkItemType.Bug:
                    if (!item.Description.Contains(svcDef.Name))
                        item.Description = $"[{svcDef.Name} | {schema}] {item.Description}";
                    item.Environment = $".NET 10, PostgreSQL 16 | Service: {svcDef.Name}, Schema: {schema}, Entities: {entityCsv}";
                    item.TechnicalNotes = $"Investigate in {ns}. Check {primaryEntity} entity, {schema} schema. Dependencies: {depsCsv}. {note}".Trim();
                    break;
            }
        }

        _logger.LogInformation("LLM enrichment mapped {Mapped}/{Total} items for module {Module}",
            mapped, items.Count, module);

        // Fallback: deterministic enrichment for items the LLM didn't map
        if (mapped < items.Count)
        {
            var unmappedItems = items.Where(i =>
                i.AffectedServices.Count == 0 ||
                string.Equals(i.Module, "General", StringComparison.OrdinalIgnoreCase)).ToList();

            if (unmappedItems.Count > 0)
            {
                _logger.LogInformation("Deterministic fallback enriching {Count} unmapped items", unmappedItems.Count);
                EnrichItemsWithServiceContext(unmappedItems, reqs, module, reqServiceMap, context);
            }
        }
    }

    /// <summary>
    /// Post-process enrichment: stamps every item with concrete service, entity, schema,
    /// namespace, and route context so downstream agents know exactly what to build.
    /// Works for ALL item types: Epic, UserStory, UseCase, Task, Bug.
    /// </summary>
    private static void EnrichItemsWithServiceContext(
        List<ExpandedRequirement> items,
        List<Requirement> reqs,
        string module,
        Dictionary<string, List<string>> reqServiceMap,
        AgentContext context)
    {
        foreach (var item in items)
        {
            // ── 1. Resolve AffectedServices if missing ──
            if (item.AffectedServices.Count == 0)
            {
                var matchingReq = reqs.FirstOrDefault(r =>
                    item.SourceRequirementId == r.Id ||
                    item.Title.Contains(r.Title, StringComparison.OrdinalIgnoreCase));
                if (matchingReq is not null && reqServiceMap.TryGetValue(matchingReq.Id, out var svcs))
                    item.AffectedServices.AddRange(svcs);
            }

            // ── 2. Resolve module from "General" ──
            if (string.Equals(item.Module, "General", StringComparison.OrdinalIgnoreCase))
            {
                var inferredSvc = InferServiceFromContent(
                    item.Title.ToLowerInvariant(),
                    (item.Description ?? "").ToLowerInvariant(),
                    context);
                if (inferredSvc is not null)
                    item.Module = inferredSvc.Replace("Service", "");
            }

            // If still General but we have AffectedServices, derive module from service
            if (string.Equals(item.Module, "General", StringComparison.OrdinalIgnoreCase)
                && item.AffectedServices.Count > 0)
            {
                item.Module = item.AffectedServices[0].Replace("Service", "");
            }

            // ── 3. Resolve service definition ──
            var primarySvcDef = item.AffectedServices.Count > 0
                ? ServiceCatalogResolver.ByName(context, item.AffectedServices[0])
                : null;

            // Try to resolve from module if no service matched
            if (primarySvcDef is null)
            {
                var svcName = InferServiceFromModule(item.Module);
                if (svcName is not null)
                {
                    primarySvcDef = ServiceCatalogResolver.ByName(context, svcName);
                    if (primarySvcDef is not null && item.AffectedServices.Count == 0)
                        item.AffectedServices.Add(svcName);
                }
            }

            if (primarySvcDef is null) continue; // Can't enrich without a service definition

            var svcLabel = primarySvcDef.Name;
            var schema = primarySvcDef.Schema;
            var entities = primarySvcDef.Entities;
            var entityCsv = string.Join(", ", entities);
            var primaryEntity = entities.Length > 0 ? entities[0] : SanitizeEntityName(item.Title);
            var ns = primarySvcDef.Namespace;
            var svcShort = primarySvcDef.ShortName;
            var depsCsv = primarySvcDef.DependsOn.Length > 0 ? string.Join(", ", primarySvcDef.DependsOn) : "none";

            // ── 4. Enrich by item type ──
            switch (item.ItemType)
            {
                case WorkItemType.Epic:
                    if (!item.Description.Contains(svcLabel))
                        item.Description = $"[{svcLabel}] {item.Description}";
                    if (string.IsNullOrWhiteSpace(item.Summary) || !item.Summary.Contains(svcLabel))
                        item.Summary = $"Implement in {svcLabel} (schema: {schema}, entities: {entityCsv}). {item.Summary}";
                    if (string.IsNullOrWhiteSpace(item.Scope) || !item.Scope.Contains(schema))
                        item.Scope = $"Scope: {svcLabel} ({schema}) — entities: {entityCsv}. {item.Scope}";
                    break;

                case WorkItemType.UserStory:
                    if (!item.Description.Contains(svcLabel))
                        item.Description = $"[{svcLabel} | {entityCsv}] {item.Description}";
                    if (string.IsNullOrWhiteSpace(item.DetailedSpec) || !item.DetailedSpec.Contains(svcLabel))
                        item.DetailedSpec = $"Service: {svcLabel}, Schema: {schema}, Entities: {entityCsv}, Dependencies: {depsCsv}. {item.DetailedSpec}";
                    break;

                case WorkItemType.UseCase:
                    if (!item.Description.Contains(svcLabel))
                        item.Description = $"[{svcLabel} | {primaryEntity}] {item.Description}";
                    if (string.IsNullOrWhiteSpace(item.Postconditions) || !item.Postconditions.Contains(schema))
                        item.Postconditions = $"{item.Postconditions} Data persisted in {schema} schema via {svcLabel}.";
                    break;

                case WorkItemType.Task:
                    if (!item.Description.Contains(svcLabel))
                        item.Description = $"[{svcLabel} | {schema}] {item.Description}";
                    // Enrich TechnicalNotes with entity/schema specifics
                    if (!string.IsNullOrWhiteSpace(item.TechnicalNotes) && !item.TechnicalNotes.Contains(schema))
                        item.TechnicalNotes = $"Service: {svcLabel}, Schema: {schema}, Entities: {entityCsv}, Namespace: {ns}. {item.TechnicalNotes}";
                    else if (string.IsNullOrWhiteSpace(item.TechnicalNotes))
                        item.TechnicalNotes = $"Service: {svcLabel}, Schema: {schema}, Entities: {entityCsv}, Namespace: {ns}, Port: {primarySvcDef.ApiPort}, Dependencies: {depsCsv}.";
                    // Enrich DetailedSpec
                    if (!string.IsNullOrWhiteSpace(item.DetailedSpec) && !item.DetailedSpec.Contains(svcLabel))
                        item.DetailedSpec = $"Target: {svcLabel} ({ns}), Schema: {schema}, Primary Entity: {primaryEntity}. {item.DetailedSpec}";
                    else if (string.IsNullOrWhiteSpace(item.DetailedSpec))
                        item.DetailedSpec = $"Target: {svcLabel} ({ns}), Schema: {schema}, Primary Entity: {primaryEntity}, Entities: {entityCsv}, API Port: {primarySvcDef.ApiPort}, Route: api/{svcShort}/{primaryEntity.ToLowerInvariant()}s.";
                    break;

                case WorkItemType.Bug:
                    if (!item.Description.Contains(svcLabel))
                        item.Description = $"[{svcLabel} | {schema}] {item.Description}";
                    if (!string.IsNullOrWhiteSpace(item.Environment) && !item.Environment.Contains(svcLabel))
                        item.Environment = $"{item.Environment} | Service: {svcLabel}, Schema: {schema}, Entities: {entityCsv}";
                    else if (string.IsNullOrWhiteSpace(item.Environment))
                        item.Environment = $".NET 10, PostgreSQL 16 | Service: {svcLabel}, Schema: {schema}, Entities: {entityCsv}";
                    if (string.IsNullOrWhiteSpace(item.TechnicalNotes))
                        item.TechnicalNotes = $"Investigate in {ns}. Check {primaryEntity} entity, {schema} schema. Dependencies: {depsCsv}.";
                    break;
            }
        }
    }

    private static string? InferServiceFromModule(string module) => null; // Service mapping is handled by LLM enrichment

    private static string SanitizeId(string id) =>
        Regex.Replace(id.Trim(), @"[^a-zA-Z0-9\-_]", "");

    private static int ParsePriority(string s) =>
        int.TryParse(s.Trim(), out var p) ? Math.Clamp(p, 1, 3) : 2;

    private static int ParseStoryPoints(string s)
    {
        if (!int.TryParse(s.Trim(), out var sp)) return 3;
        return sp is 1 or 2 or 3 or 5 or 8 ? sp : 3;
    }

    private static List<string> ParseLabels(string s)
    {
        var labels = s.Trim()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return labels.Count > 0 ? labels : ["API", "Backend"];
    }

    private static string EnsureUserStoryTemplate(string title, string fallbackContext)
    {
        var t = title.Trim();
        if (Regex.IsMatch(t, @"^As a\s+.+,\s*I want to\s+.+\s+so that\s+.+\.?$", RegexOptions.IgnoreCase))
            return t;

        var action = string.IsNullOrWhiteSpace(t) ? "complete the requested workflow" : ToSentenceFragment(t);
        var value = string.IsNullOrWhiteSpace(fallbackContext)
            ? "the system operates correctly and efficiently"
            : ToSentenceFragment(fallbackContext);

        return $"As a user, I want to {action} so that {value}.";
    }

    private static List<string> EnsureGivenWhenThenCriteria(List<string> criteria)
    {
        if (criteria.Count == 0)
        {
            return ["Given the user is authorized, when they submit a valid request, then the system processes it and records an auditable result."];
        }

        return criteria.Select(c =>
        {
            var trimmed = c.Trim();
            if (trimmed.Contains("given", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("when", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("then", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return $"Given the user is authorized, when they {ToSentenceFragment(trimmed)}, then the expected outcome is produced and auditable.";
        }).ToList();
    }

    private static string ToSentenceFragment(string value)
    {
        var v = value.Trim().Trim('.', ';', ':', ',');
        if (string.IsNullOrWhiteSpace(v)) return "complete the requested operation";
        return char.ToLowerInvariant(v[0]) + v[1..];
    }

    private static List<string> ParseSemicolonList(string s) =>
        s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int EnsureUseCasesForModuleItems(List<ExpandedRequirement> items, string module, int iteration)
    {
        var added = 0;
        var useCaseSeed = items.Count(i => i.ItemType == WorkItemType.UseCase);

        var epics = items.Where(i => i.ItemType == WorkItemType.Epic).ToList();
        if (epics.Count == 0)
        {
            var stories = items.Where(i => i.ItemType == WorkItemType.UserStory).ToList();
            foreach (var story in stories)
            {
                if (items.Any(i => i.ItemType == WorkItemType.UseCase && i.ParentId == story.ParentId))
                    continue;

                useCaseSeed++;
                items.Add(CreateTemplateUseCase(module, iteration, useCaseSeed, story.ParentId, story.Title));
                added++;
            }
            return added;
        }

        foreach (var epic in epics)
        {
            if (items.Any(i => i.ItemType == WorkItemType.UseCase && i.ParentId == epic.Id))
                continue;

            useCaseSeed++;
            items.Add(CreateTemplateUseCase(module, iteration, useCaseSeed, epic.Id, epic.Title));
            added++;
        }

        return added;
    }

    private static ExpandedRequirement CreateTemplateUseCase(string module, int iteration, int seed, string parentId, string epicTitle)
    {
        var title = EnsureUseCaseTitle(epicTitle, module);
        var mainFlow = EnsureMainFlow([], title);
        var actor = "Registered User";
        var preconditions = "User is on the relevant page and has network connectivity.";
        var postconditions = "Data changes are persisted and the workflow outcome is auditable.";

        return new ExpandedRequirement
        {
            Id = $"UC-{module}-{seed:D3}-AUTO",
            ParentId = parentId,
            ItemType = WorkItemType.UseCase,
            Title = title,
            Actor = actor,
            Preconditions = preconditions,
            MainFlow = mainFlow,
            AlternativeFlows = "Handle validation and authorization failures with user-safe messaging.",
            Postconditions = postconditions,
            Description = $"Actor: {actor}\nPreconditions: {preconditions}\nMain Flow: {string.Join("; ", mainFlow)}\nPostconditions: {postconditions}",
            Module = module,
            Priority = 2,
            Iteration = iteration,
            Status = WorkItemStatus.New,
            ProducedBy = "RequirementsExpander"
        };
    }

    private static string EnsureUseCaseTitle(string title, string module)
    {
        var t = title.Trim();
        if (!string.IsNullOrWhiteSpace(t)) return t;
        return $"Execute {module} Workflow";
    }

    private static string EnsureActor(string actor)
    {
        var a = actor.Trim();
        return string.IsNullOrWhiteSpace(a) ? "Registered User" : a;
    }

    private static string EnsurePreconditions(string preconditions)
    {
        var p = preconditions.Trim();
        return string.IsNullOrWhiteSpace(p)
            ? "User is on the relevant page and has network connectivity."
            : p;
    }

    private static List<string> EnsureMainFlow(List<string> mainFlow, string useCaseTitle)
    {
        if (mainFlow.Count >= 3)
            return RenumberMainFlow(mainFlow);

        var action = ToSentenceFragment(useCaseTitle);
        return
        [
            "1. User initiates the requested workflow.",
            "2. System prompts for required information.",
            $"3. User submits valid details to {action}.",
            "4. System validates and processes the request.",
            "5. System confirms completion to the user."
        ];
    }

    private static List<string> RenumberMainFlow(List<string> steps)
    {
        var output = new List<string>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var cleaned = Regex.Replace(steps[i].Trim(), "^\\d+\\.\\s*", string.Empty);
            output.Add($"{i + 1}. {cleaned}");
        }
        return output;
    }

    private static string EnsurePostconditions(string postconditions)
    {
        var p = postconditions.Trim();
        return string.IsNullOrWhiteSpace(p)
            ? "Data changes are persisted and the workflow outcome is auditable."
            : p;
    }

    private static string EnsureTaskTitle(string title, string idHint)
    {
        var t = title.Trim();
        if (Regex.IsMatch(t, @"^\[T-[^\]]+\]\s+.+$", RegexOptions.IgnoreCase))
            return t;

        var normalizedId = SanitizeId(idHint);
        var token = string.IsNullOrWhiteSpace(normalizedId) ? "T-001" : normalizedId;
        return $"[{token}] {t}";
    }

    private static string EnsureTaskDescription(string description, string title)
    {
        var d = description.Trim();
        if (!string.IsNullOrWhiteSpace(d)) return d;
        return $"Implement the technical work required for: {title}";
    }

    private static string EnsureTechnicalNotes(string notes, List<string> tags)
    {
        var n = notes.Trim();
        if (!string.IsNullOrWhiteSpace(n)) return n;

        if (tags.Any(t => t.Equals("database", StringComparison.OrdinalIgnoreCase)))
            return "Use PostgreSQL-friendly schema design, indexes, and tenant-safe constraints.";
        if (tags.Any(t => t.Equals("api", StringComparison.OrdinalIgnoreCase) || t.Equals("service", StringComparison.OrdinalIgnoreCase)))
            return "Implement validation, robust error handling, and auditable business operations.";
        if (tags.Any(t => t.Equals("testing", StringComparison.OrdinalIgnoreCase)))
            return "Cover positive, negative, and boundary scenarios with repeatable tests.";

        return "Follow project coding standards and ensure observability and error handling are included.";
    }

    private static List<string> EnsureTaskDefinitionOfDone(List<string> dod)
    {
        if (dod.Count >= 3) return dod;

        return
        [
            "[ ] Unit tests passed.",
            "[ ] Documentation updated in Swagger.",
            "[ ] Code reviewed by peer."
        ];
    }

    private static List<string> EnsureTaskTags(List<string> tags, string title)
    {
        if (tags.Count > 0) return tags;

        var t = title.ToLowerInvariant();
        if (t.Contains("database") || t.Contains("schema") || t.Contains("migration")) return ["database"];
        if (t.Contains("test")) return ["testing"];
        if (t.Contains("razor") || t.Contains("blazor") || t.Contains("page") || t.Contains("component") ||
            t.Contains("form") || t.Contains("dashboard") || t.Contains("layout") || t.Contains("ui ") ||
            t.Contains("frontend") || t.Contains("view")) return ["ui"];
        if (t.Contains("api") || t.Contains("endpoint")) return ["api", "service"];
        return ["service"];
    }

    private static string EnsureBugTitle(string title)
    {
        var t = title.Trim();
        if (string.IsNullOrWhiteSpace(t))
            t = "Unspecified failure in workflow";

        return t.StartsWith("[BUG]", StringComparison.OrdinalIgnoreCase)
            ? t
            : $"[BUG] {t}";
    }

    private static string EnsureBugSeverity(string severity)
    {
        var s = severity.Trim();
        if (s.Equals("Blocker", StringComparison.OrdinalIgnoreCase)) return "Blocker";
        if (s.Equals("Critical", StringComparison.OrdinalIgnoreCase) || s.Equals("High", StringComparison.OrdinalIgnoreCase)) return "Critical";
        if (s.Equals("Major", StringComparison.OrdinalIgnoreCase) || s.Equals("Medium", StringComparison.OrdinalIgnoreCase)) return "Major";
        if (s.Equals("Minor", StringComparison.OrdinalIgnoreCase) || s.Equals("Low", StringComparison.OrdinalIgnoreCase)) return "Minor";
        return "Major";
    }

    private static string EnsureBugEnvironment(string environment)
    {
        var e = environment.Trim();
        return string.IsNullOrWhiteSpace(e) ? "Development, Local" : e;
    }

    private static List<string> EnsureBugSteps(List<string> steps)
    {
        if (steps.Count == 0)
        {
            return
            [
                "1. Navigate to the impacted screen or API endpoint.",
                "2. Execute the failing action using valid preconditions.",
                "3. Observe the failure behavior and capture logs."
            ];
        }

        var normalized = new List<string>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var cleaned = Regex.Replace(steps[i].Trim(), "^\\d+\\.\\s*", string.Empty);
            normalized.Add($"{i + 1}. {cleaned}");
        }
        return normalized;
    }

    private static string EnsureBugExpected(string expected)
    {
        var e = expected.Trim();
        return string.IsNullOrWhiteSpace(e)
            ? "The system should handle the action gracefully and return a valid response."
            : e;
    }

    private static string EnsureBugActual(string actual)
    {
        var a = actual.Trim();
        return string.IsNullOrWhiteSpace(a)
            ? "The system fails to complete the action and produces an invalid or unstable outcome."
            : a;
    }

    private static string EnsureBugAttachments(string attachments)
    {
        var a = attachments.Trim();
        return string.IsNullOrWhiteSpace(a)
            ? "[Screenshot pending / Log snippet pending]"
            : a;
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    private static Requirement CloneRequirement(Requirement req) => new()
    {
        Id = req.Id,
        SourceFile = req.SourceFile,
        Section = req.Section,
        HeadingLevel = req.HeadingLevel,
        Title = req.Title,
        Description = req.Description,
        Module = req.Module,
        Tags = [.. req.Tags],
        AcceptanceCriteria = [.. req.AcceptanceCriteria],
        DependsOn = [.. req.DependsOn]
    };

    private static List<Requirement> ImproveRequirementsForInvest(
        List<Requirement> requirements,
        List<RequirementQualityScore> scores,
        int pass) => requirements; // Legacy — no longer used; kept for compilation safety

    // ─── LLM-based Quality Assessment ───────────────────────────────────────

    private sealed class QualityAssessmentResult
    {
        public int ReadyCount { get; init; }
        public int NeedsWorkCount { get; init; }
        public string ScorecardMarkdown { get; init; } = string.Empty;
        public List<string> Issues { get; init; } = [];
        /// <summary>Per-requirement quality hints the LLM expansion prompt can reference.</summary>
        public Dictionary<string, string> QualityHints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<QualityAssessmentResult> RunLlmQualityAssessmentAsync(
        List<Requirement> requirements, CancellationToken ct)
    {
        // Process in batches to avoid token limits
        const int batchSize = 40;
        var allLines = new List<string>();
        var allIssues = new List<string>();
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var readyCount = 0;
        var needsWorkCount = 0;

        for (var i = 0; i < requirements.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = requirements.Skip(i).Take(batchSize).ToList();

            var reqLines = batch.Select(r =>
                $"- {r.Id}: \"{r.Title}\" | Desc({r.Description.Length} chars): {Truncate(r.Description, 150)} | AC: {r.AcceptanceCriteria.Count} | Tags: {string.Join(",", r.Tags)} | Deps: {string.Join(",", r.DependsOn)}");

            var prompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a requirements quality analyst. Evaluate each requirement
                    against INVEST criteria for the project described in the domain context.

                    For each requirement, output exactly ONE line in this format:
                    SCORE|<req_id>|<score 0-100>|<ready YES or NO>|<one-line quality note or improvement hint>

                    Scoring guide:
                    - 90-100: Excellent — clear actor, specific behavior, measurable outcome, testable criteria
                    - 70-89: Good — minor gaps in specificity but implementable
                    - 50-69: Fair — vague language, missing actor or outcome, needs enrichment during expansion
                    - 0-49: Weak — too abstract, no testable criteria, multiple concerns bundled together

                    Be generous to domain-specific requirements that reference known business
                    workflows and processes even if they lack formal Given/When/Then formatting.
                    Real BRD requirements are valuable even without perfect syntax.

                    Output ONLY SCORE lines. No markdown. No explanations.
                    """,
                UserPrompt = $"Evaluate these {batch.Count} requirements:\n{string.Join("\n", reqLines)}",
                Temperature = 0.1,
                MaxTokens = 4096,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                foreach (var line in response.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!line.StartsWith("SCORE|", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = line.Split('|');
                    if (parts.Length < 5) continue;

                    var reqId = parts[1].Trim();
                    var scoreStr = parts[2].Trim();
                    var ready = parts[3].Trim().Equals("YES", StringComparison.OrdinalIgnoreCase);
                    var note = parts[4].Trim();

                    if (!int.TryParse(scoreStr, out var score)) score = 50;

                    allLines.Add($"- {reqId} | Score={score} | Ready={ready} | {note}");
                    if (ready) readyCount++;
                    else
                    {
                        needsWorkCount++;
                        allIssues.Add($"{reqId}: {note}");
                    }
                    hints[reqId] = note;
                }
            }
            else
            {
                // LLM failed for this batch — mark all as ready (don't block)
                _logger.LogWarning("LLM quality assessment failed for batch {Start}-{End}: {Error}",
                    i, i + batch.Count, response.Error);
                foreach (var r in batch)
                {
                    allLines.Add($"- {r.Id} | Score=N/A | Ready=Yes | LLM assessment unavailable — proceeding with expansion");
                    readyCount++;
                }
            }
        }

        var scorecard = "# Requirement Quality Scorecard (LLM-assessed)\n\n"
                        + $"Total: {requirements.Count} | Strong: {readyCount} | Needs enrichment: {needsWorkCount}\n\n"
                        + string.Join("\n", allLines);

        return new QualityAssessmentResult
        {
            ReadyCount = readyCount,
            NeedsWorkCount = needsWorkCount,
            ScorecardMarkdown = scorecard,
            Issues = allIssues,
            QualityHints = hints
        };
    }

    private static string NormalizeGivenWhenThen(string criterion)
    {
        var trimmed = criterion.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (lower.Contains("given") && lower.Contains("when") && lower.Contains("then"))
            return trimmed;

        var sentence = trimmed.TrimEnd('.', ';');
        if (string.IsNullOrWhiteSpace(sentence))
            sentence = "the requested workflow is executed";

        return $"Given the preconditions are satisfied, when {ToLowerFirst(sentence)}, then the expected outcome is produced and can be validated.";
    }

    private static string ToLowerFirst(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (value.Length == 1) return value.ToLowerInvariant();
        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
