using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Requirements;

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

        var expanded = new List<ExpandedRequirement>();
        var iteration = context.DevIteration;

        // ── Step 1: Build requirement→service mapping ──
        var reqServiceMap = BuildRequirementServiceMap(context.Requirements, context.DomainModel);
        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Mapped {context.Requirements.Count} requirements to {reqServiceMap.Values.SelectMany(v => v).Distinct().Count()} microservices");

        // ── Step 2: Build requirement dependency graph ──
        var reqDeps = BuildRequirementDependencies(context.Requirements, reqServiceMap);
        if (context.ReportProgress is not null)
        {
            var totalDeps = reqDeps.Values.Sum(d => d.Count);
            await context.ReportProgress(Type, $"Built dependency graph: {totalDeps} cross-requirement dependencies identified");
        }

        // ── Step 3: Group by module and expand ──
        var byModule = context.Requirements
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
                    
                    Output EXACTLY in this format — one item per line:
                    EPIC|<id>|<title>|<description>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>
                    STORY|<id>|<parent_id>|<title>|<acceptance_criteria_semicolon_sep>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>|<detailed_spec>
                    USECASE|<id>|<parent_id>|<title>|<actor>|<preconditions>|<main_flow_steps_numbered>|<alt_flows>|<postconditions>|<services_csv>
                    TASK|<id>|<parent_id>|<title>|<tags_csv>|<priority 1-3>|<services_csv>|<detailed_spec>
                    
                    Rules for IDs: Use module prefixes (EPIC-PAT-001, STORY-PAT-001-01, TASK-PAT-001-01-DB).
                    
                    Rules for <detailed_spec>:
                    - For database tasks: entity names, fields with types, indexes, constraints, FK relationships
                    - For service tasks: method signatures, validation rules, business logic, error handling
                    - For API tasks: HTTP method, route, request/response DTO shapes, status codes
                    - For test tasks: test scenarios, setup data, expected outcomes, edge cases
                    - For all: HIPAA implications, multi-tenant isolation, audit requirements
                    
                    Rules for dependencies:
                    - Database tasks before service tasks, service before API, API before UI
                    - Cross-service: PatientService before EncounterService, etc.
                    - Reference specific task IDs when ordering is required
                    """,
                UserPrompt = $"""
                    Module: {module}
                    Iteration: {iteration}
                    
                    === DOMAIN MODEL ===
                    {domainCtx}
                    
                    === HIGH-LEVEL REQUIREMENTS ===
                    {string.Join("\n", reqSummaries)}
                    
                    === ALREADY BUILT ({existingArtifacts.Count} artifacts) ===
                    {string.Join("\n", existingArtifacts)}
                    
                    === OPEN FINDINGS ({existingFindings.Count}) ===
                    {string.Join("\n", existingFindings)}
                    
                    Generate Epics, User Stories with acceptance criteria, Use Cases with actor/flow detail,
                    and Tasks with full specifications. Each item must include:
                    1. Which microservice(s) it affects
                    2. Dependencies on other items
                    3. Detailed spec for code generation
                    
                    Focus on gaps — don't repeat work for existing artifacts.
                    """,
                Temperature = 0.3,
                MaxTokens = 8192,
                RequestingAgent = Name
            };

            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success)
            {
                var items = ParseExpansionResponse(response.Content, module, iteration);
                // Enrich items with service mappings from requirements
                foreach (var item in items)
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
                expanded.AddRange(items);
                _logger.LogInformation("Expanded {Module}: {Count} work items from LLM", module, items.Count);
                if (context.ReportProgress is not null)
                {
                    var epics = items.Count(i => i.ItemType == WorkItemType.Epic);
                    var stories = items.Count(i => i.ItemType == WorkItemType.UserStory);
                    var tasks = items.Count(i => i.ItemType == WorkItemType.Task);
                    await context.ReportProgress(Type, $"Module '{module}' expanded: {epics} epics, {stories} stories, {tasks} tasks — LLM generated {items.Count} work items");
                }
            }
            else
            {
                _logger.LogWarning("LLM expansion failed for {Module}: {Error}", module, response.Error);
                expanded.AddRange(CreateFallbackItems(reqs, module, iteration, reqServiceMap, reqDeps));
            }
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

            // Explicit DependsOn from the requirement parsing
            depIds.AddRange(req.DependsOn);

            // Implicit dependencies from service catalog
            if (serviceMap.TryGetValue(req.Id, out var services))
            {
                foreach (var svcName in services)
                {
                    var svc = MicroserviceCatalog.ByName(svcName);
                    if (svc is null) continue;

                    // Find requirements that own the dependent services
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

        return deps;
    }

    // ─── Resolve dependencies across expanded items ────────────────────
    private static void ResolveExpandedDependencies(List<ExpandedRequirement> items, ParsedDomainModel? domainModel)
    {
        var byId = items.ToDictionary(i => i.Id);

        foreach (var item in items)
        {
            // Auto-dependencies: DB → Service → API → Test ordering
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

            // Cross-service dependencies from MicroserviceCatalog
            foreach (var svcName in item.AffectedServices)
            {
                var svc = MicroserviceCatalog.ByName(svcName);
                if (svc is null) continue;

                foreach (var dep in svc.DependsOn)
                {
                    var depItems = items.Where(i =>
                        i.Id != item.Id &&
                        i.AffectedServices.Any(s => s.Equals(dep, StringComparison.OrdinalIgnoreCase)) &&
                        i.ItemType == item.ItemType);
                    foreach (var di in depItems)
                    {
                        if (!item.DependsOn.Contains(di.Id))
                            item.DependsOn.Add(di.Id);
                    }
                }
            }

            // Build resolved chain (flattened transitive deps)
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
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;

            var kind = parts[0].Trim().ToUpperInvariant();
            switch (kind)
            {
                case "EPIC" when parts.Length >= 5:
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ItemType = WorkItemType.Epic,
                        Title = parts[2].Trim(),
                        Description = parts[3].Trim(),
                        Module = module,
                        Priority = ParsePriority(parts[4]),
                        Iteration = iteration,
                        AffectedServices = parts.Length > 5
                            ? [.. parts[5].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DependsOn = parts.Length > 6
                            ? [.. parts[6].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;

                case "STORY" when parts.Length >= 6:
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
                        AcceptanceCriteria = parts[4].Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                        AffectedServices = parts.Length > 6
                            ? [.. parts[6].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DependsOn = parts.Length > 7
                            ? [.. parts[7].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                            : [],
                        DetailedSpec = parts.Length > 8 ? parts[8].Trim() : "",
                        Status = WorkItemStatus.New,
                        ProducedBy = "RequirementsExpander"
                    });
                    break;

                case "USECASE" when parts.Length >= 8:
                    items.Add(new ExpandedRequirement
                    {
                        Id = SanitizeId(parts[1]),
                        ParentId = SanitizeId(parts[2]),
                        ItemType = WorkItemType.UseCase,
                        Title = parts[3].Trim(),
                        Description = $"Actor: {parts[4].Trim()}\nPreconditions: {parts[5].Trim()}\nMain Flow: {parts[6].Trim()}\nAlternative Flows: {(parts.Length > 7 ? parts[7].Trim() : "N/A")}\nPostconditions: {(parts.Length > 8 ? parts[8].Trim() : "N/A")}",
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
            var epicId = $"EPIC-{module}-{seq:D3}";
            var services = serviceMap.TryGetValue(req.Id, out var sl) ? sl : [];
            var deps = depMap.TryGetValue(req.Id, out var dl) ? dl : [];

            items.Add(new ExpandedRequirement
            {
                Id = epicId, ItemType = WorkItemType.Epic,
                SourceRequirementId = req.Id, Title = req.Title,
                Description = req.Description, Module = module,
                Priority = 2, Iteration = iteration,
                AffectedServices = [.. services],
                DependsOn = [.. deps],
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            var storyId = $"US-{module}-{seq:D3}-01";
            items.Add(new ExpandedRequirement
            {
                Id = storyId, ParentId = epicId,
                ItemType = WorkItemType.UserStory,
                SourceRequirementId = req.Id,
                Title = $"Implement core functionality: {req.Title}",
                AcceptanceCriteria = [.. req.AcceptanceCriteria],
                Module = module, Priority = 2, Iteration = iteration,
                AffectedServices = [.. services],
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            // Generate DB + Service + Test tasks per story
            var dbTaskId = $"TASK-{module}-{seq:D3}-01-DB";
            items.Add(new ExpandedRequirement
            {
                Id = dbTaskId, ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"Database schema for {req.Title}",
                Module = module, Priority = 3, Iteration = iteration,
                Tags = ["database"],
                AffectedServices = [.. services],
                DetailedSpec = $"Create entities and migrations for: {Truncate(req.Description, 200)}",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            items.Add(new ExpandedRequirement
            {
                Id = $"TASK-{module}-{seq:D3}-01-SVC", ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"Service implementation for {req.Title}",
                Module = module, Priority = 3, Iteration = iteration,
                Tags = ["service"],
                AffectedServices = [.. services],
                DependsOn = [dbTaskId],
                DetailedSpec = $"Implement service with CRUD, validation, domain events for: {Truncate(req.Description, 200)}",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });

            items.Add(new ExpandedRequirement
            {
                Id = $"TASK-{module}-{seq:D3}-01-TEST", ParentId = storyId,
                ItemType = WorkItemType.Task,
                Title = $"Unit tests for {req.Title}",
                Module = module, Priority = 3, Iteration = iteration,
                Tags = ["testing"],
                AffectedServices = [.. services],
                DependsOn = [$"TASK-{module}-{seq:D3}-01-SVC"],
                DetailedSpec = $"Test scenarios: happy path, validation errors, edge cases for: {Truncate(req.Description, 200)}",
                Status = WorkItemStatus.New,
                ProducedBy = "RequirementsExpander"
            });
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

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
