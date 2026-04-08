using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.GapAnalysis;

/// <summary>
/// Inspects generated code artifacts and base objects, identifies implementation gaps
/// (missing entities, endpoints, services, tests, integration contracts), and creates
/// new backlog items via the RequirementsExpander feedback loop.
///
/// Flow: GapAnalysis → creates new Requirements → triggers RequirementsExpander →
///       produces new ExpandedRequirements → Backlog picks them up → agents process them.
/// </summary>
public sealed class GapAnalysisAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<GapAnalysisAgent> _logger;

    public AgentType Type => AgentType.GapAnalysis;
    public string Name => "Gap Analysis Agent";
    public string Description => "Scans generated code and base objects, identifies missing implementations, and feeds gap items back to RequirementsExpander for backlog generation.";

    public GapAnalysisAgent(ILlmProvider llm, ILogger<GapAnalysisAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("GapAnalysisAgent starting — {Arts} artifacts, {Reqs} requirements, {Backlog} backlog items",
            context.Artifacts.Count, context.Requirements.Count, context.ExpandedRequirements.Count);

        var artifacts = context.Artifacts.ToList();
        var expandedItems = context.ExpandedRequirements.ToList();
        var gapRequirements = new List<Requirement>();

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Scanning {artifacts.Count} artifacts across {GetServiceCount(artifacts)} services for implementation gaps");

        // ── Step 1: Catalog what exists ──
        var serviceArtifacts = CatalogArtifactsByService(artifacts);
        var existingEntities = ExtractEntityNames(artifacts);
        var existingEndpoints = ExtractEndpointPaths(artifacts);
        var existingTests = ExtractTestCoverage(artifacts);
        var existingIntegrations = ExtractIntegrationContracts(artifacts);

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type,
                $"Cataloged: {existingEntities.Count} entities, {existingEndpoints.Count} endpoints, {existingTests.Count} test files, {existingIntegrations.Count} integration contracts");

        // ── Step 2: Identify gaps per service ──
        var allGaps = new List<IdentifiedGap>();

        foreach (var (service, layers) in serviceArtifacts)
        {
            ct.ThrowIfCancellationRequested();
            var serviceGaps = AnalyzeServiceGaps(service, layers, existingEntities, existingEndpoints, existingTests, existingIntegrations, context);
            allGaps.AddRange(serviceGaps);
        }

        // ── Step 3: Cross-service integration gaps ──
        var integrationGaps = AnalyzeCrossServiceGaps(serviceArtifacts, existingIntegrations, context);
        allGaps.AddRange(integrationGaps);

        // ── Step 4: Domain model vs artifact gaps ──
        if (context.DomainModel is not null)
        {
            var domainGaps = AnalyzeDomainModelGaps(context.DomainModel, existingEntities, existingEndpoints, serviceArtifacts);
            allGaps.AddRange(domainGaps);
        }

        // ── Step 5: Backlog coverage gaps — items that were never processed ──
        var orphanGaps = AnalyzeOrphanBacklogItems(expandedItems, artifacts);
        allGaps.AddRange(orphanGaps);

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Identified {allGaps.Count} gaps: {allGaps.Count(g => g.Category == "Integration")} integration, {allGaps.Count(g => g.Category == "Entity")} entity, {allGaps.Count(g => g.Category == "Endpoint")} endpoint, {allGaps.Count(g => g.Category == "Test")} test");

        // ── Step 6: Convert gaps into Requirements and feed to RequirementsExpander ──
        var newReqCount = 0;
        foreach (var gap in allGaps.DistinctBy(g => g.Title))
        {
            var req = new Requirement
            {
                Id = $"GAP-{gap.Category[..Math.Min(3, gap.Category.Length)].ToUpperInvariant()}-{newReqCount + 1:D4}",
                Title = gap.Title,
                Description = gap.Description,
                Module = gap.Module,
                Tags = [gap.Category.ToLowerInvariant(), "gap-analysis", .. gap.AffectedServices.Take(3)],
                AcceptanceCriteria = gap.AcceptanceCriteria,
                DependsOn = gap.DependsOn
            };
            gapRequirements.Add(req);
            context.Requirements.Add(req);
            newReqCount++;
        }

        _logger.LogInformation("GapAnalysis identified {GapCount} gaps → created {ReqCount} new requirements",
            allGaps.Count, newReqCount);

        // ── Step 7: Send directive to RequirementsExpander to process new gap requirements ──
        if (newReqCount > 0)
        {
            context.DirectiveQueue.Enqueue(new AgentDirective
            {
                From = Type,
                To = AgentType.RequirementsExpander,
                Action = "EXPAND_GAPS",
                Details = $"GapAnalysis created {newReqCount} new requirements from {allGaps.Count} identified gaps",
                Priority = 1
            });

            context.DirectiveQueue.Enqueue(new AgentDirective
            {
                From = Type,
                To = AgentType.Backlog,
                Action = "REFRESH_BACKLOG",
                Details = $"GapAnalysis added {newReqCount} gap-closing requirements"
            });
        }

        // ── Step 8: Record findings ──
        foreach (var gap in allGaps.Take(100))
        {
            context.Findings.Add(new ReviewFinding
            {
                Category = "GapAnalysis",
                Severity = gap.Severity,
                Message = $"[{gap.Category}] {gap.Title}",
                FilePath = gap.AffectedServices.FirstOrDefault() ?? "cross-service",
                Suggestion = gap.Description
            });
        }

        // ── Step 9: Generate gap analysis report artifact ──
        var reportContent = BuildGapReport(allGaps, serviceArtifacts, gapRequirements);
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "docs/gap-analysis-report.md",
            FileName = "gap-analysis-report.md",
            Namespace = "Hms.GapAnalysis",
            ProducedBy = Type,
            Content = reportContent
        });

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Gap analysis complete: {allGaps.Count} gaps → {newReqCount} new requirements fed to expansion pipeline");

        context.AgentStatuses[Type] = AgentStatus.Completed;
        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"GapAnalysis: {allGaps.Count} gaps identified → {newReqCount} new requirements created for expansion",
            Duration = sw.Elapsed
        };
    }

    // ─── Artifact Cataloging ───────────────────────────────────────────

    private static Dictionary<string, Dictionary<string, List<CodeArtifact>>> CatalogArtifactsByService(List<CodeArtifact> artifacts)
    {
        var result = new Dictionary<string, Dictionary<string, List<CodeArtifact>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var art in artifacts)
        {
            var service = ExtractServiceName(art);
            if (!result.ContainsKey(service))
                result[service] = new Dictionary<string, List<CodeArtifact>>(StringComparer.OrdinalIgnoreCase);

            var layer = art.Layer.ToString();
            if (!result[service].ContainsKey(layer))
                result[service][layer] = [];

            result[service][layer].Add(art);
        }

        return result;
    }

    private static string ExtractServiceName(CodeArtifact art)
    {
        // Try to extract from relative path: Hms.PatientService/... → PatientService
        var match = Regex.Match(art.RelativePath, @"Hms\.(\w+Service)", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        // Try from namespace
        match = Regex.Match(art.Namespace, @"Hms\.(\w+Service)", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        // Fallback: SharedKernel, Infrastructure, etc.
        if (art.RelativePath.Contains("SharedKernel", StringComparison.OrdinalIgnoreCase)) return "SharedKernel";
        if (art.RelativePath.Contains("ApiGateway", StringComparison.OrdinalIgnoreCase)) return "ApiGateway";

        return "Other";
    }

    private static HashSet<string> ExtractEntityNames(List<CodeArtifact> artifacts)
    {
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var art in artifacts.Where(a => a.Layer == ArtifactLayer.Database))
        {
            // Match class declarations for entities
            foreach (Match m in Regex.Matches(art.Content, @"class\s+(\w+)\s*(?::\s*\w+)?", RegexOptions.IgnoreCase))
                entities.Add(m.Groups[1].Value);
            // Match table names from CREATE TABLE
            foreach (Match m in Regex.Matches(art.Content, @"CREATE\s+TABLE\s+(?:\w+\.)?(\w+)", RegexOptions.IgnoreCase))
                entities.Add(m.Groups[1].Value);
        }
        return entities;
    }

    private static HashSet<string> ExtractEndpointPaths(List<CodeArtifact> artifacts)
    {
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var art in artifacts.Where(a => a.Layer is ArtifactLayer.Service or ArtifactLayer.Infrastructure))
        {
            // Match MapGet/MapPost/MapPut/MapDelete patterns
            foreach (Match m in Regex.Matches(art.Content, @"Map(?:Get|Post|Put|Delete|Patch)\s*\(\s*""([^""]+)"""))
                endpoints.Add(m.Groups[1].Value);
            // Match [HttpGet("/path")] attributes
            foreach (Match m in Regex.Matches(art.Content, @"\[Http(?:Get|Post|Put|Delete|Patch)\s*\(\s*""([^""]+)""\s*\)\]"))
                endpoints.Add(m.Groups[1].Value);
        }
        return endpoints;
    }

    private static HashSet<string> ExtractTestCoverage(List<CodeArtifact> artifacts)
    {
        var tests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var art in artifacts.Where(a => a.Layer == ArtifactLayer.Test))
            tests.Add(art.FileName);
        return tests;
    }

    private static HashSet<string> ExtractIntegrationContracts(List<CodeArtifact> artifacts)
    {
        var contracts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var art in artifacts)
        {
            // Kafka topic definitions
            foreach (Match m in Regex.Matches(art.Content, @"""([\w\.\-]+\.events\.[\w\.\-]+)""", RegexOptions.IgnoreCase))
                contracts.Add(m.Groups[1].Value);
            // Event class names
            foreach (Match m in Regex.Matches(art.Content, @"class\s+(\w+(?:Event|Message|Command|IntegrationEvent))\b"))
                contracts.Add(m.Groups[1].Value);
        }
        return contracts;
    }

    // ─── Gap Analysis Logic ────────────────────────────────────────────

    private static List<IdentifiedGap> AnalyzeServiceGaps(
        string service,
        Dictionary<string, List<CodeArtifact>> layers,
        HashSet<string> existingEntities,
        HashSet<string> existingEndpoints,
        HashSet<string> existingTests,
        HashSet<string> existingIntegrations,
        AgentContext context)
    {
        var gaps = new List<IdentifiedGap>();

        // Gap: Service has DB layer but no service layer
        if (layers.ContainsKey("Database") && !layers.ContainsKey("Service"))
        {
            gaps.Add(new IdentifiedGap
            {
                Category = "Service",
                Title = $"Missing service layer for {service}",
                Description = $"{service} has database artifacts but no service/business logic layer. Need CRUD services, validation, and domain events.",
                Module = service.Replace("Service", ""),
                AffectedServices = [service],
                Severity = ReviewSeverity.Error,
                AcceptanceCriteria = [$"Given {service} entities exist, when service layer is generated, then all CRUD operations are available with validation"]
            });
        }

        // Gap: Service has no API endpoints
        if (!layers.ContainsKey("Application") && layers.ContainsKey("Service"))
        {
            gaps.Add(new IdentifiedGap
            {
                Category = "Endpoint",
                Title = $"Missing API endpoints for {service}",
                Description = $"{service} has service logic but no HTTP API endpoints. Need REST endpoints with proper DTOs and error handling.",
                Module = service.Replace("Service", ""),
                AffectedServices = [service],
                Severity = ReviewSeverity.Error,
                AcceptanceCriteria = [$"Given {service} services exist, when API endpoints are created, then CRUD operations are exposed via REST with proper status codes"]
            });
        }

        // Gap: No test coverage for this service
        var hasTests = existingTests.Any(t => t.Contains(service.Replace("Service", ""), StringComparison.OrdinalIgnoreCase));
        if (!hasTests && (layers.ContainsKey("Database") || layers.ContainsKey("Service")))
        {
            gaps.Add(new IdentifiedGap
            {
                Category = "Test",
                Title = $"Missing test coverage for {service}",
                Description = $"No automated tests found for {service}. Need unit tests for services, integration tests for endpoints, and validation tests.",
                Module = service.Replace("Service", ""),
                AffectedServices = [service],
                Severity = ReviewSeverity.Warning,
                AcceptanceCriteria = [$"Given {service} is implemented, when tests are run, then at least 80% of business logic is covered"]
            });
        }

        // Gap: No Kafka consumer/producer for services that should have them
        if (service != "SharedKernel" && service != "ApiGateway" && service != "Other")
        {
            var hasIntegration = existingIntegrations.Any(c => c.Contains(service.Replace("Service", ""), StringComparison.OrdinalIgnoreCase));
            if (!hasIntegration)
            {
                gaps.Add(new IdentifiedGap
                {
                    Category = "Integration",
                    Title = $"Missing event integration for {service}",
                    Description = $"{service} lacks Kafka event producer/consumer. Cross-service communication requires event-driven integration.",
                    Module = service.Replace("Service", ""),
                    AffectedServices = [service],
                    Severity = ReviewSeverity.Warning,
                    AcceptanceCriteria = [$"Given {service} produces domain events, when state changes occur, then events are published to Kafka for downstream consumers"]
                });
            }
        }

        // Gap: Health check missing
        var hasHealthCheck = layers.Values.SelectMany(l => l).Any(a =>
            a.FileName.Contains("HealthCheck", StringComparison.OrdinalIgnoreCase) ||
            a.Content.Contains("IHealthCheck", StringComparison.OrdinalIgnoreCase));
        if (!hasHealthCheck && service != "SharedKernel" && service != "Other")
        {
            gaps.Add(new IdentifiedGap
            {
                Category = "Infrastructure",
                Title = $"Missing health check for {service}",
                Description = $"{service} has no health check endpoint. Required for container orchestration readiness/liveness probes.",
                Module = service.Replace("Service", ""),
                AffectedServices = [service],
                Severity = ReviewSeverity.Warning,
                AcceptanceCriteria = [$"Given {service} is deployed, when /health is called, then it returns healthy/unhealthy status with dependency checks"]
            });
        }

        return gaps;
    }

    private static List<IdentifiedGap> AnalyzeCrossServiceGaps(
        Dictionary<string, Dictionary<string, List<CodeArtifact>>> serviceArtifacts,
        HashSet<string> existingIntegrations,
        AgentContext context)
    {
        var gaps = new List<IdentifiedGap>();
        var services = serviceArtifacts.Keys.Where(s => s != "SharedKernel" && s != "ApiGateway" && s != "Other").ToList();

        // Check for missing cross-service event contracts
        var expectedIntegrations = new[]
        {
            ("PatientService", "EncounterService", "patient.events.registered"),
            ("EncounterService", "InpatientService", "encounter.events.admitted"),
            ("EncounterService", "DiagnosticsService", "encounter.events.order-placed"),
            ("DiagnosticsService", "EncounterService", "diagnostics.events.result-ready"),
            ("InpatientService", "RevenueService", "inpatient.events.discharged"),
            ("EmergencyService", "EncounterService", "emergency.events.triage-completed"),
            ("RevenueService", "AuditService", "revenue.events.claim-submitted"),
        };

        foreach (var (from, to, topic) in expectedIntegrations)
        {
            if (!serviceArtifacts.ContainsKey(from) || !serviceArtifacts.ContainsKey(to))
                continue;

            var hasContract = existingIntegrations.Any(c => c.Contains(topic, StringComparison.OrdinalIgnoreCase));
            if (!hasContract)
            {
                gaps.Add(new IdentifiedGap
                {
                    Category = "Integration",
                    Title = $"Missing event contract: {from} → {to} ({topic})",
                    Description = $"Expected Kafka event topic '{topic}' from {from} to {to} is not implemented. This cross-service integration is needed for the end-to-end patient workflow.",
                    Module = "Integration",
                    AffectedServices = [from, to],
                    Severity = ReviewSeverity.Error,
                    AcceptanceCriteria =
                    [
                        $"Given {from} publishes to '{topic}', when a domain event occurs, then {to} receives and processes the event",
                        $"Given the event contract exists, when messages are published, then they conform to the shared schema with correlation IDs"
                    ],
                    DependsOn = [$"GAP-INT-{from}", $"GAP-INT-{to}"]
                });
            }
        }

        // Gap: No API Gateway routing for any service
        if (!serviceArtifacts.ContainsKey("ApiGateway") || !serviceArtifacts["ApiGateway"].Values.SelectMany(a => a).Any(a =>
            a.Content.Contains("ReverseProxy", StringComparison.OrdinalIgnoreCase) || a.Content.Contains("ProxyRoute", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var svc in services.Where(s => s.EndsWith("Service")))
            {
                gaps.Add(new IdentifiedGap
                {
                    Category = "Integration",
                    Title = $"Missing API Gateway route for {svc}",
                    Description = $"API Gateway has no reverse-proxy route to {svc}. External clients cannot reach {svc} endpoints.",
                    Module = "Integration",
                    AffectedServices = ["ApiGateway", svc],
                    Severity = ReviewSeverity.Warning,
                    AcceptanceCriteria = [$"Given {svc} exposes REST endpoints, when accessed via API Gateway, then requests are routed correctly with auth headers"]
                });
            }
        }

        return gaps;
    }

    private static List<IdentifiedGap> AnalyzeDomainModelGaps(
        ParsedDomainModel domainModel,
        HashSet<string> existingEntities,
        HashSet<string> existingEndpoints,
        Dictionary<string, Dictionary<string, List<CodeArtifact>>> serviceArtifacts)
    {
        var gaps = new List<IdentifiedGap>();

        // Check domain model entities against actual generated entities
        foreach (var entity in domainModel.Entities)
        {
            if (!existingEntities.Contains(entity.Name) && !existingEntities.Contains(entity.Name + "Entity"))
            {
                gaps.Add(new IdentifiedGap
                {
                    Category = "Entity",
                    Title = $"Missing entity: {entity.Name} ({entity.ServiceName})",
                    Description = $"Domain model defines entity '{entity.Name}' in {entity.ServiceName} with {entity.Fields.Count} fields, but no database artifact generates it.",
                    Module = entity.ServiceName.Replace("Service", ""),
                    AffectedServices = [entity.ServiceName],
                    Severity = ReviewSeverity.Error,
                    AcceptanceCriteria =
                    [
                        $"Given the domain model specifies {entity.Name}, when database migration runs, then the table exists with all required columns",
                        $"Given {entity.Name} entity exists, when CRUD operations are performed, then tenant isolation and audit fields are enforced"
                    ]
                });
            }
        }

        // Check domain model endpoints against actual generated endpoints
        foreach (var ep in domainModel.ApiEndpoints)
        {
            var pathMatch = existingEndpoints.Any(e => e.Contains(ep.Path.Split('/').Last(), StringComparison.OrdinalIgnoreCase));
            if (!pathMatch)
            {
                gaps.Add(new IdentifiedGap
                {
                    Category = "Endpoint",
                    Title = $"Missing endpoint: {ep.Method} {ep.Path} ({ep.ServiceName})",
                    Description = $"Domain model specifies {ep.Method} {ep.Path} ({ep.OperationName}) in {ep.ServiceName}, but no matching API endpoint was generated.",
                    Module = ep.ServiceName.Replace("Service", ""),
                    AffectedServices = [ep.ServiceName],
                    Severity = ReviewSeverity.Warning,
                    AcceptanceCriteria = [$"Given the API spec defines {ep.Method} {ep.Path}, when the endpoint is called, then it returns the expected response with proper status codes"]
                });
            }
        }

        return gaps;
    }

    private static List<IdentifiedGap> AnalyzeOrphanBacklogItems(
        List<ExpandedRequirement> expandedItems,
        List<CodeArtifact> artifacts)
    {
        var gaps = new List<IdentifiedGap>();
        var artifactModules = artifacts.Select(a => a.Namespace.Split('.').LastOrDefault() ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find tasks that are still New/InQueue with no artifacts matching their module
        var orphanTasks = expandedItems
            .Where(e => e.ItemType == WorkItemType.Task &&
                        e.Status is WorkItemStatus.New or WorkItemStatus.InQueue &&
                        !artifactModules.Contains(e.Module))
            .GroupBy(e => e.Module)
            .ToList();

        foreach (var group in orphanTasks)
        {
            if (group.Count() < 3) continue; // Only flag modules with significant unprocessed items

            gaps.Add(new IdentifiedGap
            {
                Category = "Coverage",
                Title = $"Unprocessed backlog: {group.Count()} tasks in module '{group.Key}'",
                Description = $"Module '{group.Key}' has {group.Count()} tasks that remain New/InQueue with no matching artifacts. These items were never claimed by any agent.",
                Module = group.Key,
                AffectedServices = group.SelectMany(g => g.AffectedServices).Distinct().Take(5).ToList(),
                Severity = ReviewSeverity.Warning,
                AcceptanceCriteria = [$"Given {group.Count()} unprocessed tasks exist in '{group.Key}', when agents re-run, then at least the database and service tasks produce artifacts"]
            });
        }

        return gaps;
    }

    // ─── Report Builder ────────────────────────────────────────────────

    private static string BuildGapReport(
        List<IdentifiedGap> gaps,
        Dictionary<string, Dictionary<string, List<CodeArtifact>>> serviceArtifacts,
        List<Requirement> gapRequirements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Gap Analysis Report");
        sb.AppendLine($"**Generated**: {DateTime.UtcNow:u}");
        sb.AppendLine($"**Total Gaps**: {gaps.Count}");
        sb.AppendLine($"**New Requirements Created**: {gapRequirements.Count}");
        sb.AppendLine();

        sb.AppendLine("## Service Inventory");
        foreach (var (svc, layers) in serviceArtifacts.OrderBy(k => k.Key))
        {
            var layerSummary = string.Join(", ", layers.Select(l => $"{l.Key}({l.Value.Count})"));
            sb.AppendLine($"- **{svc}**: {layerSummary}");
        }
        sb.AppendLine();

        sb.AppendLine("## Gaps by Category");
        foreach (var group in gaps.GroupBy(g => g.Category).OrderBy(g => g.Key))
        {
            sb.AppendLine($"### {group.Key} ({group.Count()})");
            foreach (var gap in group.Take(20))
            {
                var sev = gap.Severity switch
                {
                    ReviewSeverity.Error => "🔴",
                    ReviewSeverity.Critical => "🔴",
                    ReviewSeverity.Warning => "🟡",
                    _ => "🔵"
                };
                sb.AppendLine($"- {sev} **{gap.Title}**");
                sb.AppendLine($"  {gap.Description}");
                if (gap.AffectedServices.Count > 0)
                    sb.AppendLine($"  Services: {string.Join(", ", gap.AffectedServices)}");
            }
            sb.AppendLine();
        }

        if (gapRequirements.Count > 0)
        {
            sb.AppendLine("## New Requirements for Expansion");
            foreach (var req in gapRequirements.Take(50))
            {
                sb.AppendLine($"- **{req.Id}**: {req.Title}");
                sb.AppendLine($"  Tags: {string.Join(", ", req.Tags)} | Module: {req.Module}");
            }
        }

        return sb.ToString();
    }

    private static int GetServiceCount(List<CodeArtifact> artifacts) =>
        artifacts.Select(a => ExtractServiceName(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    // ─── Gap Data Model ────────────────────────────────────────────────

    private sealed class IdentifiedGap
    {
        public string Category { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Module { get; init; } = string.Empty;
        public List<string> AffectedServices { get; init; } = [];
        public ReviewSeverity Severity { get; init; } = ReviewSeverity.Warning;
        public List<string> AcceptanceCriteria { get; init; } = [];
        public List<string> DependsOn { get; init; } = [];
    }
}
