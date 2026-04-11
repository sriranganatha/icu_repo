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
    private const int MaxInvestImprovementIterations = 10;
    private const double MinInvestReadyRatio = 0.35;
    private const int MinInvestReadyCount = 10;

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

        // ── Step 0: INVEST quality scoring gate ──
        var workingRequirements = context.Requirements.Select(CloneRequirement).ToList();
        List<RequirementQualityScore> qualityScores = [];
        var readyRequirements = new List<Requirement>();
        var investIterationsUsed = 0;

        for (var pass = 0; pass <= MaxInvestImprovementIterations; pass++)
        {
            qualityScores = RequirementQualityScorer.ScoreAll(workingRequirements);
            var readyRequirementIds = qualityScores
                .Where(s => s.IsReady)
                .Select(s => s.RequirementId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            readyRequirements = workingRequirements
                .Where(r => readyRequirementIds.Contains(r.Id))
                .ToList();

            var readyRatio = workingRequirements.Count == 0
                ? 0
                : (double)readyRequirements.Count / workingRequirements.Count;
            var readyCountThreshold = Math.Min(MinInvestReadyCount, workingRequirements.Count);
            var gateSatisfied = readyRequirements.Count >= readyCountThreshold && readyRatio >= MinInvestReadyRatio;

            if (gateSatisfied)
            {
                investIterationsUsed = pass;
                break;
            }

            if (pass == MaxInvestImprovementIterations)
            {
                investIterationsUsed = pass;
                break;
            }

            workingRequirements = ImproveRequirementsForInvest(workingRequirements, qualityScores, pass + 1);
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"INVEST auto-improvement pass {pass + 1}/{MaxInvestImprovementIterations} completed — retrying quality gate");
        }

        // Persist improved requirement content into context so downstream agents and DB snapshots use it.
        context.Requirements = workingRequirements;

        var failed = qualityScores.Where(s => !s.IsReady).ToList();
        foreach (var fail in failed.Take(50))
        {
            context.Findings.Add(new ReviewFinding
            {
                Category = "RequirementQuality",
                Severity = ReviewSeverity.Warning,
                Message = $"Requirement {fail.RequirementId} not INVEST-ready (score {fail.Score}/100): {string.Join(" | ", fail.Notes.Take(3))}",
                FilePath = "docs/requirements",
                Suggestion = "Refine this requirement using INVEST and Given/When/Then acceptance criteria before expansion."
            });
        }

        var scorecardLines = qualityScores.Select(s =>
            $"- {s.RequirementId} | Score={s.Score} | Ready={s.IsReady} | I={s.Independent} N={s.Negotiable} V={s.Valuable} E={s.Estimable} S={s.Small} T={s.Testable}");
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "Requirements/requirement-quality-scorecard.md",
            FileName = "requirement-quality-scorecard.md",
            Namespace = "GNex.Requirements",
            ProducedBy = Type,
            Content = "# Requirement Quality Scorecard (INVEST)\n\n"
                      + $"Total: {qualityScores.Count} | Ready: {qualityScores.Count(s => s.IsReady)} | Blocked: {failed.Count} | Improvement Passes: {investIterationsUsed}\n\n"
                      + string.Join("\n", scorecardLines)
        });

        var ambiguousRequirements = qualityScores
            .Where(s => s.ClarifyingQuestions.Count > 0)
            .ToList();
        if (ambiguousRequirements.Count > 0)
        {
            var clarificationLines = ambiguousRequirements.SelectMany(s =>
            {
                var header = $"## {s.RequirementId} - {s.Title}";
                var questions = s.ClarifyingQuestions.Select((q, i) => $"{i + 1}. {q}");
                return new[] { header, "" }.Concat(questions).Concat([""]); 
            });

            context.Artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "Requirements/clarification-questions.md",
                FileName = "clarification-questions.md",
                Namespace = "GNex.Requirements",
                ProducedBy = Type,
                Content = "# Requirement Clarification Questions\n\n"
                          + "These requirements contain ambiguous language and need explicit answers before implementation.\n\n"
                          + string.Join("\n", clarificationLines)
            });

            foreach (var item in ambiguousRequirements.Take(50))
            {
                context.Findings.Add(new ReviewFinding
                {
                    Category = "RequirementAmbiguity",
                    Severity = ReviewSeverity.Warning,
                    Message = $"Requirement {item.RequirementId} needs clarification before implementation readiness.",
                    FilePath = "docs/requirements",
                    Suggestion = item.ClarifyingQuestions.FirstOrDefault()
                                 ?? "Answer the clarification questions in Requirements/clarification-questions.md."
                });
            }
        }

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Requirement quality gate: {qualityScores.Count(s => s.IsReady)}/{qualityScores.Count} ready for expansion");

        var minimumReadyForSelectiveExpansion = Math.Max(5, workingRequirements.Count / 5);
        if (readyRequirements.Count < minimumReadyForSelectiveExpansion)
        {
            readyRequirements = workingRequirements;
            context.Findings.Add(new ReviewFinding
            {
                Category = "RequirementQuality",
                Severity = ReviewSeverity.Warning,
                Message = $"RequirementsExpander produced only {qualityScores.Count(s => s.IsReady)}/{qualityScores.Count} INVEST-ready requirements after {investIterationsUsed} improvement pass(es). Proceeding with improved draft requirements to avoid starvation.",
                FilePath = "docs/requirements",
                Suggestion = "Review requirement-quality-scorecard.md and refine low-scoring requirements for higher-fidelity expansion."
            });

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"INVEST ready set is too small for selective expansion — proceeding with all improved requirements");
        }

        // ── Step 1: Build requirement→service mapping ──
        var reqServiceMap = BuildRequirementServiceMap(readyRequirements, context.DomainModel);
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Mapped {readyRequirements.Count} quality-approved requirements to {reqServiceMap.Values.SelectMany(v => v).Distinct().Count()} microservices");

        // ── Step 2: Build requirement dependency graph ──
        var reqDeps = BuildRequirementDependencies(readyRequirements, reqServiceMap);
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

            // Build context snapshot of what exists
            var existingArtifacts = context.Artifacts
                .Where(a => a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase))
                .Select(a => $"  - [{a.Layer}] {a.RelativePath} (by {a.ProducedBy})")
                .Take(25)
                .ToList();

            var existingFindings = context.Findings
                .Where(f => f.FilePath?.Contains(module, StringComparison.OrdinalIgnoreCase) ?? false)
                .Select(f => $"  - [{f.Severity}] {f.Category}: {f.Message}")
                .Take(15)
                .ToList();

            // Build rich requirement summaries with service mappings and dependencies
            var reqSummaries = reqs.Select(r =>
            {
                var services = reqServiceMap.TryGetValue(r.Id, out var slist) ? string.Join(", ", slist) : "Unknown";
                var deps = reqDeps.TryGetValue(r.Id, out var dlist) ? string.Join(", ", dlist) : "None";
                return $"""
                    - {r.Id}: {r.Title}
                      Description: {Truncate(r.Description, 300)}
                      Acceptance Criteria: {string.Join("; ", r.AcceptanceCriteria)}
                      Tags: {string.Join(", ", r.Tags)}
                      Affected Services: {services}
                      Depends On: {deps}
                    """;
            });

            // Domain model context for the LLM
            var domainCtx = BuildDomainContextForModule(module, context.DomainModel);

            var prompt = new LlmPrompt
            {
                SystemPrompt = """
                    You are a senior healthcare software architect expanding requirements for
                    an ICU Hospital Management System built on .NET 8 microservices.

                    You produce implementation-ready work items that downstream code-generation agents
                    will use DIRECTLY. Each item must contain enough detail to generate code without
                    further clarification.

                    ─── DECOMPOSITION PHILOSOPHY ──────────────────────────────────
                    You MUST use VERTICAL (user-value) slicing, NOT horizontal (technical-layer) slicing.

                    BAD (horizontal — do NOT do this):
                      "Build the database schema" / "Create the API layer" / "Build the frontend"
                    GOOD (vertical — each story delivers end-to-end user value):
                      "As a nurse, I can search patients by MRN" (touches DB + API + UI)
                      "As a doctor, I can view patient vitals dashboard" (touches DB + API + UI)
                      "As an admin, I can manage ward bed assignments" (touches DB + API + UI)

                    ─── EPIC → STORIES SPLITTING CHECKLIST ────────────────────────
                    When splitting an Epic into Stories, ask:
                    1. Who are the different user personas? (each persona = different stories)
                    2. What is the smallest thing I can ship that this user would value?
                    3. Can I demo this story in isolation?
                    4. Each story must be independently deployable and demo-able
                    5. A story that touches only one layer (just backend, just UI) is a TASK, not a story
                    6. Aim for stories completable in 1-3 days by a small team (1-5 story points)

                    For EACH Epic, generate AT LEAST 3-5 User Stories (more for complex epics).

                    ─── STORY → TASKS SPLITTING CHECKLIST ─────────────────────────
                    When splitting a Story into Tasks, ask:
                    1. What are the interfaces/contracts? (define these FIRST)
                    2. What are the independent implementation tracks? (API contract, backend, frontend, infra)
                    3. What needs testing beyond unit tests? (integration, E2E, contract tests)
                    4. What are the failure modes? (error handling, retries, observability)

                    For EACH User Story, generate AT LEAST 5-7 Tasks covering:
                      - Define API contract (request/response schema, OpenAPI spec)
                      - Implement database entities/migrations/indexes
                      - Build service layer with validation and business logic
                      - Build API endpoint wiring service to HTTP
                      - Build Razor Page / Blazor UI (list page, detail/edit form, navigation)
                      - Write integration tests for the endpoint
                      - Write E2E test for the full user flow

                    IMPORTANT: This is a FULL-STACK web application. Every user-facing story
                    MUST include a UI task tagged with "ui". The UI task creates Razor Pages
                    or Blazor components that call the API endpoints. Do NOT skip the UI layer.

                    Tasks should be 2-8 hours of work, assignable to one person, with clear done state.

                    ─── ANTI-PATTERNS TO AVOID ────────────────────────────────────
                    - Stories that are just "implement X service" (no visible user value)
                    - Tasks with no clear done state ("research caching options")
                    - Skipping contract/schema tasks (leads to integration pain)
                    - Putting all testing in one task at the end (test as you go)
                    - Generating only 1 story per requirement (requirements are broad; split them)

                    ─── INVEST CRITERIA (mandatory for every User Story) ──────────
                    - Independent: can be developed without waiting on others (except explicit deps)
                    - Negotiable: detail the WHAT and WHY, not prescriptive HOW
                    - Valuable: delivers measurable user or business value
                    - Estimable: enough context for a developer to estimate effort
                    - Small: fits within one sprint (1-5 story points); split larger work
                    - Testable: has clear Given/When/Then acceptance criteria

                    ─── MINIMUM OUTPUT EXPECTATIONS ───────────────────────────────
                    For each high-level requirement, you MUST produce AT MINIMUM:
                    - 1 Epic
                    - 3-5 User Stories (each with "As a [persona], I want [action] so that [value]")
                    - 1 Use Case per Epic (actor/system interaction flow)
                    - 5-7 Tasks per User Story (contract, DB, service, API, UI/Razor page, integration test, E2E test)

                    That means for a module with 3 requirements, expect ~3 epics, ~12 stories,
                    ~3 use cases, and ~72 tasks = ~90 items total. DO NOT produce fewer.
                    Every story MUST have a UI task with tag "ui" that builds the Razor Page or Blazor component.

                    ─── OUTPUT FORMAT ──────────────────────────────────────────────
                    Output EXACTLY in this pipe-delimited format — one item per line. NO markdown,
                    NO explanatory text, NO blank lines. ONLY pipe-delimited lines.
                    Use semicolons to separate list items within a field. Use numbered
                    prefixes (1. 2. 3.) for ordered steps.

                    EPIC|<id>|<title>|<summary>|<business_value>|<success_criteria_semicolon_sep>|<scope>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>
                    STORY|<id>|<parent_epic_id>|<title>|<acceptance_criteria_semicolon_sep>|<story_points>|<labels_csv>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>|<detailed_spec>
                    USECASE|<id>|<parent_epic_id>|<title>|<actor>|<preconditions>|<main_flow_steps_semicolon_sep>|<alt_flows>|<postconditions>|<services_csv>
                    TASK|<id>|<parent_story_id>|<title>|<description>|<technical_notes>|<definition_of_done_semicolon_sep>|<tags_csv>|<priority 1-3>|<services_csv>|<detailed_spec>
                    BUG|<id>|<parent_id>|<title>|<severity>|<environment>|<steps_to_reproduce_semicolon_sep>|<expected_result>|<actual_result>|<services_csv>

                    Field rules:
                    - STORY title: MUST be "As a [Persona], I want to [Action] so that [Value/Benefit]"
                    - STORY acceptance_criteria: Each in Given/When/Then format, semicolon-separated
                    - STORY story_points: 1, 2, 3, 5, or 8
                    - USECASE main_flow: Numbered steps "1. step;2. step;3. step"
                    - TASK title: "[T-ID] Verb-noun action" e.g. "[T-PAT-001-01-API] Define POST /patients contract"
                    - TASK definition_of_done: Checklist items, semicolon-separated

                    ─── ID RULES ──────────────────────────────────────────────────
                    Use module prefixes: E-PAT-001, US-PAT-001-01, UC-PAT-001-01, T-PAT-001-01-DB, BUG-PAT-001.
                    Stories reference their parent Epic. Tasks reference their parent Story.

                    ─── DETAILED SPEC RULES ───────────────────────────────────────
                    - Database tasks: entity names, fields with types, indexes, constraints, FK relationships
                    - Service tasks: method signatures, validation rules, business logic, error handling
                    - API tasks: HTTP method, route, request/response DTO shapes, status codes
                    - Test tasks: test scenarios, setup data, expected outcomes, edge cases
                    - All items: HIPAA implications, multi-tenant isolation, audit trail requirements

                    ─── DEPENDENCY RULES ──────────────────────────────────────────
                    Within a story: Contract → DB → Service → API → UI/Razor Page → Integration Test → E2E Test
                    Cross-service: PatientService before EncounterService, etc.
                    Reference specific task IDs in depends_on_ids when ordering matters.
                    """,
                UserPrompt = $"""
                    Module: {module}
                    Iteration: {iteration}

                    === DOMAIN MODEL ===
                    {domainCtx}

                    === HIGH-LEVEL REQUIREMENTS ({reqs.Count} requirements) ===
                    {string.Join("\n", reqSummaries)}

                    === ALREADY BUILT ({existingArtifacts.Count} artifacts) ===
                    {string.Join("\n", existingArtifacts)}

                    === OPEN FINDINGS ({existingFindings.Count}) ===
                    {string.Join("\n", existingFindings)}

                    ─── YOUR TASK ─────────────────────────────────────────────────
                    Expand the {reqs.Count} requirements above into a COMPREHENSIVE work breakdown.

                    Step 1 — IDENTIFY PERSONAS: List every distinct user type (Nurse, Doctor, Admin,
                    Lab Technician, Pharmacist, Billing Clerk, System/Integration, etc.)

                    Step 2 — CREATE EPICS: One Epic per high-level requirement.

                    Step 3 — SPLIT EPICS INTO STORIES using vertical slicing:
                    For each Epic, ask "What are the different things different users need?"
                    Generate 3-5 User Stories per Epic. Each story should be independently
                    deployable and demo-able. Use "As a [persona], I want to [action] so that [benefit]".

                    Step 4 — CREATE USE CASES: One Use Case per Epic showing the primary
                    actor/system interaction flow with numbered steps.

                    Step 5 — DECOMPOSE STORIES INTO TASKS:
                    For each Story, generate 5-7 Tasks:
                      1. Define API contract/schema (OpenAPI spec, request/response DTOs) — tag: contract
                      2. Database entity/migration/index — tag: database
                      3. Service layer (validation, business logic, domain events) — tag: service
                      4. API endpoint (controller, routing, error responses) — tag: api
                      5. Razor Page / Blazor UI (list page, detail view, create/edit forms) — tag: ui
                      6. Integration tests (happy path, validation errors, auth failures) — tag: testing
                      7. E2E test (full user flow from request to database verification) — tag: testing

                    CRITICAL: Step 5 is MANDATORY for every user-facing story. This is a full-stack
                    web application — every story must have a UI task that builds pages/forms/dashboards.

                    Step 6 — BUG REPORTS: Only if open findings indicate reproducible failures.

                    CRITICAL: Generate ALL items as pipe-delimited lines. No markdown. No explanations.
                    Expect to generate 50-100+ lines for a typical module with 3+ requirements.
                    Focus on gaps — don't repeat work for existing artifacts.
                    """,
                Temperature = 0.3,
                MaxTokens = 16384,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            List<ExpandedRequirement> moduleItems;
            if (response.Success)
            {
                _logger.LogInformation("LLM response for module '{Module}': Model={Model}, Len={Len}, Preview={Preview}",
                    module, response.Model, response.Content.Length,
                    response.Content.Length > 300 ? response.Content[..300] : response.Content);
                moduleItems = ParseExpansionResponse(response.Content, module, iteration);
                if (moduleItems.Count == 0)
                {
                    _logger.LogWarning("LLM expansion for {Module} returned unparseable/empty content. Using deterministic fallback.", module);
                    moduleItems = CreateFallbackItems(reqs, module, iteration, reqServiceMap, reqDeps);
                    context.Findings.Add(new ReviewFinding
                    {
                        Category = "RequirementExpansion",
                        Severity = ReviewSeverity.Warning,
                        Message = $"LLM returned 0 parseable work items for module '{module}'. Fallback expansion generated {moduleItems.Count} items.",
                        FilePath = "Requirements/requirements-expander",
                        Suggestion = "Inspect LLM output format for this module and ensure pipe-delimited templates are respected."
                    });
                }
                // Enrich ALL items with service/entity/schema context — LLM primary, deterministic fallback
                await EnrichItemsWithLlmAsync(moduleItems, reqs, module, reqServiceMap, ct);
                _logger.LogInformation("Expanded {Module}: {Count} work items from LLM", module, moduleItems.Count);
                if (context.ReportProgress is not null)
                {
                    var epics = moduleItems.Count(i => i.ItemType == WorkItemType.Epic);
                    var stories = moduleItems.Count(i => i.ItemType == WorkItemType.UserStory);
                    var tasks = moduleItems.Count(i => i.ItemType == WorkItemType.Task);
                    await context.ReportProgress(Type, $"Module '{module}' expanded: {epics} epics, {stories} stories, {tasks} tasks — LLM generated {moduleItems.Count} work items");
                }
            }
            else
            {
                _logger.LogWarning("LLM expansion failed for {Module}: {Error}", module, response.Error);
                moduleItems = CreateFallbackItems(reqs, module, iteration, reqServiceMap, reqDeps);
            }

            expanded.AddRange(moduleItems);
        }

        // ── Step 3b: Deduplicate expanded items (same ID can appear from overlapping modules/re-runs) ──
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        expanded = expanded.Where(i => seenIds.Add(i.Id)).ToList();

        // ── Step 4: Resolve dependency chains across all expanded items ──
        ResolveExpandedDependencies(expanded, context.DomainModel);
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

    // ─── Requirement→Service Mapping ───────────────────────────────────
    private static Dictionary<string, List<string>> BuildRequirementServiceMap(
        List<Requirement> requirements, ParsedDomainModel? domainModel)
    {
        var map = new Dictionary<string, List<string>>();

        foreach (var req in requirements)
        {
            var services = new List<string>();
            var lower = $"{req.Title} {req.Description}".ToLowerInvariant();

            foreach (var svc in MicroserviceCatalog.All)
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
        List<Requirement> requirements, Dictionary<string, List<string>> serviceMap)
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
                    var svc = MicroserviceCatalog.ByName(svcName);
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
    private static void ResolveExpandedDependencies(List<ExpandedRequirement> items, ParsedDomainModel? domainModel)
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
                    var svc = MicroserviceCatalog.ByName(svcName);
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
        Dictionary<string, List<string>> depMap)
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
                var inferredSvc = InferServiceFromContent(reqTitleLower, req.Description.ToLowerInvariant());
                if (inferredSvc is not null)
                {
                    resolvedModule = inferredSvc.Replace("Service", "");
                    if (services.Count == 0) services = [inferredSvc];
                }
            }

            // Resolve the primary MicroserviceDefinition for rich context
            var primarySvcDef = services.Count > 0
                ? MicroserviceCatalog.ByName(services[0])
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
                Title = $"[{uiTaskId}] Build {primaryEntity} Razor Pages (list, detail, form) in {svcName}",
                Description = $"Create Razor Pages for {primaryEntity} management: Index (paginated list with search/filter), Detail (read-only view), Create/Edit (form with validation). Call {svcName} API at /{routeBase}. Use shared components (DataTable, FormSection, PatientCard) and hms-theme.css.",
                Module = resolvedModule, Priority = 2, Iteration = iteration,
                Tags = ["ui"],
                AffectedServices = [.. services],
                DependsOn = [apiTaskId],
                TechnicalNotes = $"Pages in src/GNex.Web/Pages/{resolvedModule}/{primaryEntity}/. Index.cshtml (list with DataTable component, search, filter by status), Details.cshtml (read-only summary), Create.cshtml and Edit.cshtml (forms with FluentValidation client-side mirrors). Use HttpClient to call {svcName} API at /{routeBase}. Bootstrap 5.3 responsive layout. WCAG 2.1 AA accessible (ARIA labels, keyboard nav, focus management). Include breadcrumb navigation.",
                DefinitionOfDone = [$"{primaryEntity} list page renders with pagination and search", $"{primaryEntity} create form submits and shows success toast", $"{primaryEntity} edit form loads existing data and saves changes", "Responsive layout works on mobile and desktop", "WCAG 2.1 AA: all form fields have labels, ARIA attributes present", "Navigation menu includes {primaryEntity} link"],
                DetailedSpec = $"Pages: Pages/{resolvedModule}/{primaryEntity}/Index.cshtml (list), Pages/{resolvedModule}/{primaryEntity}/Details.cshtml (view), Pages/{resolvedModule}/{primaryEntity}/Create.cshtml (form), Pages/{resolvedModule}/{primaryEntity}/Edit.cshtml (form). PageModel classes inject HttpClient configured for {svcName} base URL. Use IHttpClientFactory with named client '{svcShort}-api'. DTOs: reuse {primaryEntity}ResponseDto, {primaryEntity}CreateDto, {primaryEntity}UpdateDto from API contracts. Layout: _Layout.cshtml with sidebar nav. CSS: hms-theme.css variables.",
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
    private static string? InferServiceFromContent(string titleLower, string descLower)
    {
        var combined = $"{titleLower} {descLower}";
        foreach (var svc in MicroserviceCatalog.All)
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
            return "doctor";
        if (reqTitleLower.Contains("admin") || reqTitleLower.Contains("config") || reqTitleLower.Contains("permission"))
            return "administrator";
        if (reqTitleLower.Contains("billing") || reqTitleLower.Contains("claim") || reqTitleLower.Contains("invoice"))
            return "billing clerk";
        return "nurse";
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
        CancellationToken ct)
    {
        if (items.Count == 0) return;

        // Build a compact catalog summary for the prompt
        var catalogLines = MicroserviceCatalog.All.Select(s =>
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
                You are a healthcare software architect mapping work items to concrete microservices.

                Given a list of work items and a microservice catalog, determine which service(s)
                each item belongs to. For EACH item, output a single pipe-delimited line:

                <item_id>|<service_name>|<primary_entity>|<enrichment_note>

                Rules:
                - service_name: MUST be one of the service names from the catalog (e.g. PatientService)
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
            EnrichItemsWithServiceContext(items, reqs, module, reqServiceMap);
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

            var svcDef = MicroserviceCatalog.ByName(svcName);
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
                EnrichItemsWithServiceContext(unmappedItems, reqs, module, reqServiceMap);
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
        Dictionary<string, List<string>> reqServiceMap)
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
                    (item.Description ?? "").ToLowerInvariant());
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
                ? MicroserviceCatalog.ByName(item.AffectedServices[0])
                : null;

            // Try to resolve from module if no service matched
            if (primarySvcDef is null)
            {
                var svcName = InferServiceFromModule(item.Module);
                if (svcName is not null)
                {
                    primarySvcDef = MicroserviceCatalog.ByName(svcName);
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

    private static string? InferServiceFromModule(string module) => module.ToLowerInvariant() switch
    {
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
            ? "patient care workflows are safe and efficient"
            : ToSentenceFragment(fallbackContext);

        return $"As a clinical user, I want to {action} so that {value}.";
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
        return string.IsNullOrWhiteSpace(e) ? ".NET 8, PostgreSQL 16, Local" : e;
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
        int pass)
    {
        var scoreById = new Dictionary<string, RequirementQualityScore>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scores)
            scoreById.TryAdd(s.RequirementId, s);
        var improved = new List<Requirement>(requirements.Count);

        foreach (var req in requirements)
        {
            if (!scoreById.TryGetValue(req.Id, out var score) || score.IsReady)
            {
                improved.Add(req);
                continue;
            }

            var title = req.Title;
            if (title.Length < 6)
                title = $"{req.Module} requirement {req.Id}";

            var description = req.Description;
            if (description.Length < 40)
            {
                description = $"{req.Title}. This requirement must deliver measurable value, preserve safety constraints, and support traceable implementation and testing in the {req.Module} module.";
            }

            var criteria = req.AcceptanceCriteria
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Take(7)
                .ToList();

            if (criteria.Count == 0)
            {
                criteria =
                [
                    "Given a valid request context, when the user performs the required action, then the system completes it successfully and persists the result.",
                    "Given invalid or incomplete input, when the action is attempted, then the system rejects the request with a clear validation message.",
                    "Given an authorized actor executes the flow, when an update occurs, then an audit record is captured with timestamp and actor identity."
                ];
            }
            else
            {
                criteria = criteria.Select(NormalizeGivenWhenThen).ToList();
                if (criteria.Count < 2)
                {
                    criteria.Add("Given a downstream dependency is unavailable, when the request is processed, then the system returns a deterministic recoverable error and records telemetry.");
                }
            }

            improved.Add(new Requirement
            {
                Id = req.Id,
                SourceFile = req.SourceFile,
                Section = req.Section,
                HeadingLevel = req.HeadingLevel,
                Title = title,
                Description = description,
                Module = req.Module,
                Tags = [.. req.Tags],
                AcceptanceCriteria = [.. criteria],
                DependsOn = [.. req.DependsOn]
            });
        }

        return improved;
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
