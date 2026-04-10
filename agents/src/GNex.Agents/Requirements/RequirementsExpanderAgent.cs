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

                    For EACH User Story, generate AT LEAST 4-6 Tasks covering:
                      - Define API contract (request/response schema, OpenAPI spec)
                      - Implement database entities/migrations/indexes
                      - Build service layer with validation and business logic
                      - Build API endpoint wiring service to HTTP
                      - Write integration tests for the endpoint
                      - Write E2E test for the full user flow

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
                    - 4-6 Tasks per User Story (contract, DB, service, API, integration test, E2E test)

                    That means for a module with 3 requirements, expect ~3 epics, ~12 stories,
                    ~3 use cases, and ~60 tasks = ~78 items total. DO NOT produce fewer.

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
                    Within a story: Contract → DB → Service → API → Integration Test → E2E Test
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
                    For each Story, generate 4-6 Tasks:
                      1. Define API contract/schema (OpenAPI spec, request/response DTOs)
                      2. Database entity/migration/index
                      3. Service layer (validation, business logic, domain events)
                      4. API endpoint (controller, routing, error responses)
                      5. Integration tests (happy path, validation errors, auth failures)
                      6. E2E test (full user flow from request to database verification)

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
                // Enrich items with service mappings from requirements
                foreach (var item in moduleItems)
                {
                    if (item.AffectedServices.Count == 0)
                    {
                        var matchingReq = reqs.FirstOrDefault(r =>
                            item.SourceRequirementId == r.Id ||
                            item.Title.Contains(r.Title, StringComparison.OrdinalIgnoreCase));
                        if (matchingReq is not null && reqServiceMap.TryGetValue(matchingReq.Id, out var svcs))
                            item.AffectedServices.AddRange(svcs);
                    }
                }
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

            var addedUseCases = EnsureUseCasesForModuleItems(moduleItems, module, iteration);
            if (addedUseCases > 0 && context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Module '{module}' enforcement: synthesized {addedUseCases} use case(s) to satisfy template coverage");

            expanded.AddRange(moduleItems);
        }

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
        var byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

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

        // Persona-based vertical slices for healthcare ICU context
        var slices = new[]
        {
            (Persona: "nurse", Action: "search and view", Value: "I can quickly find information during care delivery", Pts: 3, Labels: new List<string> { "Frontend", "API", "Database" }),
            (Persona: "doctor", Action: "create and update", Value: "clinical documentation is accurate and up-to-date", Pts: 5, Labels: new List<string> { "Frontend", "API", "Database", "Validation" }),
            (Persona: "admin", Action: "manage configuration and permissions for", Value: "the system meets facility operational policies", Pts: 3, Labels: new List<string> { "Frontend", "API", "Security" }),
            (Persona: "system", Action: "validate data integrity and generate audit trails for", Value: "HIPAA regulatory compliance is maintained", Pts: 2, Labels: new List<string> { "API", "Database", "Security" }),
            (Persona: "billing clerk", Action: "export and report on", Value: "revenue cycle operations run accurately", Pts: 3, Labels: new List<string> { "Frontend", "API", "Reporting" })
        };

        foreach (var req in reqs)
        {
            seq++;
            var epicId = $"EPIC-{module}-{seq:D3}";
            var services = serviceMap.TryGetValue(req.Id, out var sl) ? sl : [];
            var deps = depMap.TryGetValue(req.Id, out var dl) ? dl : [];
            var reqTitleLower = req.Title.ToLowerInvariant();

            // ── Epic ──
            items.Add(new ExpandedRequirement
            {
                Id = epicId, ItemType = WorkItemType.Epic,
                SourceRequirementId = req.Id, Title = req.Title,
                Description = req.Description, Module = module,
                Priority = 2, Iteration = iteration,
                Summary = req.Description,
                BusinessValue = "Improves clinical workflow efficiency and patient safety for this module.",
                SuccessCriteria = req.AcceptanceCriteria.Count > 0
                    ? [.. req.AcceptanceCriteria.Take(5)]
                    : ["Feature fully operational", "All acceptance criteria verified", "HIPAA compliance validated", "Audit trail complete"],
                Scope = "Includes all CRUD operations, validation, and audit for this feature; excludes unrelated module refactors.",
                AffectedServices = [.. services],
                DependsOn = [.. deps],
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            // ── Use Case ──
            items.Add(new ExpandedRequirement
            {
                Id = $"UC-{module}-{seq:D3}-01",
                ParentId = epicId,
                ItemType = WorkItemType.UseCase,
                Title = $"Execute {req.Title} Workflow",
                Actor = "Nurse",
                Preconditions = "User is authenticated and has appropriate role permissions",
                MainFlow =
                [
                    $"1. User navigates to the {req.Title} screen",
                    "2. System displays the relevant data list with search/filter",
                    "3. User selects or creates a record",
                    "4. System validates input against business rules",
                    "5. User confirms the action",
                    "6. System persists changes and creates audit log entry",
                    "7. System displays confirmation with updated data"
                ],
                AlternativeFlows = "Invalid input shows validation errors; Unauthorized user sees access denied; Network failure shows retry option",
                Postconditions = "Data is persisted in database; Audit trail entry created; User sees confirmation",
                Description = $"End-to-end workflow for {req.Title} covering primary and alternative flows",
                Module = module, Priority = 2, Iteration = iteration,
                AffectedServices = [.. services],
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            // ── User Stories (one per persona slice) ──
            var storySeq = 0;
            foreach (var slice in slices)
            {
                storySeq++;
                var storyId = $"US-{module}-{seq:D3}-{storySeq:D2}";
                var storyAction = $"{slice.Action} {reqTitleLower} records";

                items.Add(new ExpandedRequirement
                {
                    Id = storyId, ParentId = epicId,
                    ItemType = WorkItemType.UserStory,
                    SourceRequirementId = req.Id,
                    Title = $"As a {slice.Persona}, I want to {storyAction} so that {slice.Value}",
                    AcceptanceCriteria =
                    [
                        $"Given the {slice.Persona} is authenticated, when they {storyAction}, then the system processes the request and displays results within 2 seconds",
                        "Given invalid input is submitted, when the system validates, then clear error messages guide the user to correct the issue",
                        "Given the operation completes, when the audit service records the action, then a complete audit trail entry exists"
                    ],
                    StoryPoints = slice.Pts,
                    Labels = [.. slice.Labels],
                    Description = $"End-to-end capability for a {slice.Persona} to {storyAction}",
                    Module = module, Priority = 2, Iteration = iteration,
                    AffectedServices = [.. services],
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });

                // ── 6 Tasks per story (vertical-slice decomposition) ──
                var contractId = $"TASK-{module}-{seq:D3}-{storySeq:D2}-CONTRACT";
                var dbTaskId = $"TASK-{module}-{seq:D3}-{storySeq:D2}-DB";
                var svcTaskId = $"TASK-{module}-{seq:D3}-{storySeq:D2}-SVC";
                var apiTaskId = $"TASK-{module}-{seq:D3}-{storySeq:D2}-API";
                var intTestId = $"TASK-{module}-{seq:D3}-{storySeq:D2}-ITEST";
                var e2eTestId = $"TASK-{module}-{seq:D3}-{storySeq:D2}-E2E";

                items.Add(new ExpandedRequirement
                {
                    Id = contractId, ParentId = storyId,
                    ItemType = WorkItemType.Task,
                    Title = $"[{contractId}] Define API contract for {storyAction}",
                    Description = $"Define OpenAPI specification with request/response DTOs, routes, status codes, and validation schemas for {storyAction}.",
                    Module = module, Priority = 2, Iteration = iteration,
                    Tags = ["api", "contract"],
                    AffectedServices = [.. services],
                    TechnicalNotes = "Use OpenAPI 3.1 spec; Include X-Tenant-Id header; Define 200, 400, 401, 403, 404, 422 responses.",
                    DefinitionOfDone = ["OpenAPI spec reviewed and approved", "DTO classes generated", "Contract tests written"],
                    DetailedSpec = $"Define request DTO with required fields and validation attributes, response DTO with pagination. REST conventions for routes.",
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });

                items.Add(new ExpandedRequirement
                {
                    Id = dbTaskId, ParentId = storyId,
                    ItemType = WorkItemType.Task,
                    Title = $"[{dbTaskId}] Implement database entities and migrations for {req.Title}",
                    Description = $"Create EF Core entities, DbContext configuration, indexes, and migration for {req.Title}.",
                    Module = module, Priority = 2, Iteration = iteration,
                    Tags = ["database"],
                    AffectedServices = [.. services],
                    DependsOn = [contractId],
                    TechnicalNotes = "PostgreSQL-friendly types; tenant isolation (TenantId FK); index lookup columns; soft-delete support.",
                    DefinitionOfDone = ["Migration runs without errors", "Indexes verified", "Seed data present", "Rollback tested"],
                    DetailedSpec = $"Entity with Id (text PK), TenantId, CreatedAt, UpdatedAt, IsActive, plus domain fields. Indexes on TenantId and lookups.",
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });

                items.Add(new ExpandedRequirement
                {
                    Id = svcTaskId, ParentId = storyId,
                    ItemType = WorkItemType.Task,
                    Title = $"[{svcTaskId}] Implement service layer with validation for {storyAction}",
                    Description = $"Build service class with CRUD operations, FluentValidation rules, domain events, and multi-tenant filtering for {storyAction}.",
                    Module = module, Priority = 2, Iteration = iteration,
                    Tags = ["service"],
                    AffectedServices = [.. services],
                    DependsOn = [dbTaskId],
                    TechnicalNotes = "Inject IRepository; Use FluentValidation; Emit domain events for audit; All queries filtered by TenantId.",
                    DefinitionOfDone = ["Unit tests passing (>80% coverage)", "Validation rules tested", "Domain events emitted", "Exception handling complete"],
                    DetailedSpec = $"Service implements interface. Methods: GetByIdAsync, ListAsync (paginated), CreateAsync, UpdateAsync, SoftDeleteAsync. All operations log to audit trail.",
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });

                items.Add(new ExpandedRequirement
                {
                    Id = apiTaskId, ParentId = storyId,
                    ItemType = WorkItemType.Task,
                    Title = $"[{apiTaskId}] Build API endpoint for {storyAction}",
                    Description = $"Create ASP.NET Core controller with route mapping, model binding, authorization attributes, and error responses for {storyAction}.",
                    Module = module, Priority = 2, Iteration = iteration,
                    Tags = ["api"],
                    AffectedServices = [.. services],
                    DependsOn = [svcTaskId],
                    TechnicalNotes = "Use [Authorize] with role policy; Map service exceptions to HTTP status codes; Add response caching for reads.",
                    DefinitionOfDone = ["All routes return correct status codes", "Authorization tested", "Swagger documentation complete", "Rate limiting configured"],
                    DetailedSpec = $"Controller with GET (list+detail), POST, PUT, DELETE endpoints. Use ProblemDetails for errors. Add [ProducesResponseType] for Swagger.",
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });

                items.Add(new ExpandedRequirement
                {
                    Id = intTestId, ParentId = storyId,
                    ItemType = WorkItemType.Task,
                    Title = $"[{intTestId}] Write integration tests for {storyAction}",
                    Description = $"Create integration tests covering happy path, validation errors, auth failures, not-found, and concurrent access for {storyAction}.",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["testing"],
                    AffectedServices = [.. services],
                    DependsOn = [apiTaskId],
                    TechnicalNotes = "Use WebApplicationFactory; Test with real DB (in-memory or TestContainers); Cover auth bypass and tenant isolation.",
                    DefinitionOfDone = ["Happy path tested", "Validation error responses tested", "401/403 tested", "404 tested", "Concurrent writes tested"],
                    DetailedSpec = $"Test scenarios: create+read round-trip, duplicate prevention, invalid input (422), unauthorized (401), forbidden (403), not found (404), optimistic concurrency.",
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });

                items.Add(new ExpandedRequirement
                {
                    Id = e2eTestId, ParentId = storyId,
                    ItemType = WorkItemType.Task,
                    Title = $"[{e2eTestId}] Write E2E test for {slice.Persona} {storyAction} flow",
                    Description = $"Create end-to-end test simulating the complete {slice.Persona} journey from login through {storyAction} to verification.",
                    Module = module, Priority = 3, Iteration = iteration,
                    Tags = ["testing", "e2e"],
                    AffectedServices = [.. services],
                    DependsOn = [intTestId],
                    TechnicalNotes = "Use Playwright or similar; Test against staging-like environment; Verify database state after operations.",
                    DefinitionOfDone = ["Complete user journey tested", "Data persistence verified", "Audit log entries verified", "Performance within SLA"],
                    DetailedSpec = $"E2E flow: authenticate as {slice.Persona} -> navigate to feature -> perform {storyAction} -> verify UI feedback -> verify DB records -> verify audit log entry.",
                    Status = WorkItemStatus.New,
                    ProducedBy = "RequirementsExpander"
                });
            }
        }

        return items;
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
        var scoreById = scores.ToDictionary(s => s.RequirementId, StringComparer.OrdinalIgnoreCase);
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
