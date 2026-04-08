using System.Text;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.ContextBrokering;

/// <summary>
/// Resolves inter-agent context queries by inspecting the shared <see cref="AgentContext"/>.
/// This gives code-gen agents the ability to "ask" other agents for schema details,
/// API contracts, integration events, domain rules, and compliance constraints —
/// enabling coordinated, context-aware code generation across the agent ecosystem.
/// </summary>
public sealed class ContextBroker : IContextBroker
{
    private readonly ILogger<ContextBroker> _logger;

    public ContextBroker(ILogger<ContextBroker> logger) => _logger = logger;

    public Task<ContextResponse> ResolveAsync(ContextQuery query, AgentContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("[ContextBroker] {From} → {To}: {Intent} ({Module}/{Entity})",
            query.From, query.To, query.Intent, query.Module, query.EntityName);

        var response = query.Intent switch
        {
            QueryIntent.EntitySchema          => ResolveEntitySchema(query, context),
            QueryIntent.ApiContract           => ResolveApiContract(query, context),
            QueryIntent.IntegrationContract   => ResolveIntegrationContract(query, context),
            QueryIntent.SecurityRequirements  => ResolveSecurityRequirements(query, context),
            QueryIntent.DomainRules           => ResolveDomainRules(query, context),
            QueryIntent.ComplianceConstraints => ResolveComplianceConstraints(query, context),
            QueryIntent.ImplementationStatus  => ResolveImplementationStatus(query, context),
            QueryIntent.ArchitectureDecision  => ResolveArchitectureDecision(query, context),
            QueryIntent.DependencyInfo        => ResolveDependencyInfo(query, context),
            QueryIntent.TestStrategy          => ResolveTestStrategy(query, context),
            _ => new ContextResponse { QueryId = query.Id, Success = false, Answer = $"Unknown intent: {query.Intent}" }
        };

        _logger.LogInformation("[ContextBroker] Resolved {QueryId}: {Success} — {SnippetCount} snippets, {FactCount} facts",
            query.Id, response.Success, response.CodeSnippets.Count, response.Facts.Count);

        return Task.FromResult(response);
    }

    // ─── Entity Schema: extract field names, types, relationships from DB artifacts ──

    private static ContextResponse ResolveEntitySchema(ContextQuery query, AgentContext context)
    {
        var entity = query.EntityName;
        var module = query.Module;
        var facts = new Dictionary<string, string>();
        var snippets = new List<string>();
        var relatedIds = new List<string>();

        // Search domain model first
        if (context.DomainModel is not null)
        {
            var ent = context.DomainModel.Entities
                .FirstOrDefault(e => e.Name.Equals(entity, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(module) || e.ServiceName.Contains(module, StringComparison.OrdinalIgnoreCase)));

            if (ent is not null)
            {
                facts["EntityName"] = ent.Name;
                facts["ServiceName"] = ent.ServiceName;
                facts["FieldCount"] = ent.Fields.Count.ToString();
                facts["Fields"] = string.Join(", ", ent.Fields.Select(f => $"{f.Name}:{f.Type}"));
                facts["Schema"] = ent.Schema;
            }
        }

        // Search database artifacts for entity class code
        var dbArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Database &&
                        a.FileName.Contains(entity, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(module) || a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var art in dbArtifacts)
        {
            snippets.Add(art.Content);
            relatedIds.Add(art.Id);
        }

        // Also include repository artifacts
        var repoArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Repository &&
                        a.FileName.Contains(entity, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var art in repoArtifacts)
        {
            snippets.Add(art.Content);
            relatedIds.Add(art.Id);
        }

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Database,
            Success = facts.Count > 0 || snippets.Count > 0,
            Answer = facts.TryGetValue("Fields", out var fields)
                ? $"Entity {entity} has fields: {fields}"
                : $"No schema found for entity {entity} in module {module}",
            CodeSnippets = snippets,
            Facts = facts,
            RelatedArtifactIds = relatedIds
        };
    }

    // ─── API Contract: extract endpoints, DTOs, validation from service/app artifacts ──

    private static ContextResponse ResolveApiContract(ContextQuery query, AgentContext context)
    {
        var module = query.Module;
        var entity = query.EntityName;
        var facts = new Dictionary<string, string>();
        var snippets = new List<string>();
        var relatedIds = new List<string>();

        // Find DTO artifacts
        var dtoArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Dto &&
                        (a.FileName.Contains(entity, StringComparison.OrdinalIgnoreCase) ||
                         a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var art in dtoArtifacts)
        {
            snippets.Add(art.Content);
            relatedIds.Add(art.Id);
            facts[$"DTO:{art.FileName}"] = $"Layer={art.Layer}, Namespace={art.Namespace}";
        }

        // Find service interface artifacts
        var svcArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Service &&
                        a.FileName.StartsWith("I", StringComparison.Ordinal) &&
                        (a.FileName.Contains(entity, StringComparison.OrdinalIgnoreCase) ||
                         a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var art in svcArtifacts)
        {
            snippets.Add(art.Content);
            relatedIds.Add(art.Id);
        }

        // Check domain model for endpoints
        if (context.DomainModel is not null)
        {
            var endpoints = context.DomainModel.ApiEndpoints
                .Where(e => e.ServiceName.Contains(module, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(entity) && e.EntityName.Contains(entity, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (endpoints.Count > 0)
            {
                facts["Endpoints"] = string.Join("; ", endpoints.Select(e => $"{e.Method} {e.Path}"));
                facts["EndpointCount"] = endpoints.Count.ToString();
            }
        }

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.ServiceLayer,
            Success = snippets.Count > 0 || facts.Count > 0,
            Answer = snippets.Count > 0
                ? $"Found {dtoArtifacts.Count} DTOs and {svcArtifacts.Count} service interfaces for {module}/{entity}"
                : $"No API contract found for {module}/{entity}",
            CodeSnippets = snippets,
            Facts = facts,
            RelatedArtifactIds = relatedIds
        };
    }

    // ─── Integration Contract: events, topics, consumer groups ──

    private static ContextResponse ResolveIntegrationContract(ContextQuery query, AgentContext context)
    {
        var module = query.Module;
        var facts = new Dictionary<string, string>();
        var snippets = new List<string>();
        var relatedIds = new List<string>();

        // Find Kafka/integration artifacts
        var intArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Integration &&
                        a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var art in intArtifacts)
        {
            snippets.Add(art.Content);
            relatedIds.Add(art.Id);
        }

        // Check domain model for events
        if (context.DomainModel is not null)
        {
            var events = context.DomainModel.DomainEvents
                .Where(e => e.ServiceName.Contains(module, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (events.Count > 0)
                facts["Events"] = string.Join(", ", events.Select(e => e.EventName));

            var svcDef = MicroserviceCatalog.ByName(module) ??
                MicroserviceCatalog.All.FirstOrDefault(s => s.Name.Contains(module, StringComparison.OrdinalIgnoreCase));
            if (svcDef is not null)
            {
                facts["DependsOn"] = string.Join(", ", svcDef.DependsOn);
            }
        }

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Integration,
            Success = snippets.Count > 0 || facts.Count > 0,
            Answer = $"Integration: {intArtifacts.Count} artifacts for {module}",
            CodeSnippets = snippets,
            Facts = facts,
            RelatedArtifactIds = relatedIds
        };
    }

    // ─── Security Requirements: PHI fields, auth rules, data classification ──

    private static ContextResponse ResolveSecurityRequirements(ContextQuery query, AgentContext context)
    {
        var entity = query.EntityName;
        var facts = new Dictionary<string, string>();
        var snippets = new List<string>();

        // Check for security-related findings
        var secFindings = context.Findings
            .Where(f => f.Category.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
                        f.Category.Contains("OWASP", StringComparison.OrdinalIgnoreCase) ||
                        f.Category.Contains("HIPAA", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (secFindings.Count > 0)
            facts["SecurityFindings"] = string.Join("; ", secFindings.Take(10).Select(f => f.Message));

        // Check for security artifacts
        var secArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Security &&
                        (string.IsNullOrEmpty(entity) || a.FileName.Contains(entity, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var art in secArtifacts.Take(5))
            snippets.Add(art.Content);

        // PHI field detection from entity schema
        var phiFields = new[] { "SSN", "DateOfBirth", "MedicalRecord", "Insurance", "Diagnosis", "Medication",
            "LabResult", "VitalSign", "Allergy", "PatientName", "Email", "Phone", "Address" };

        if (context.DomainModel is not null && !string.IsNullOrEmpty(entity))
        {
            var allEntities = context.DomainModel.Entities.AsEnumerable();
            var ent = allEntities.FirstOrDefault(e => e.Name.Equals(entity, StringComparison.OrdinalIgnoreCase));
            if (ent is not null)
            {
                var phiMatches = ent.Fields
                    .Where(f => phiFields.Any(phi => f.Name.Contains(phi, StringComparison.OrdinalIgnoreCase)))
                    .Select(f => f.Name)
                    .ToList();
                if (phiMatches.Count > 0)
                    facts["PHI_Fields"] = string.Join(", ", phiMatches);
            }
        }

        facts["RequiresEncryption"] = "true";
        facts["RequiresAuditLog"] = "true";
        facts["RequiresTenantIsolation"] = "true";

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Security,
            Success = true,
            Answer = $"Security: {secFindings.Count} findings, PHI fields: {facts.GetValueOrDefault("PHI_Fields", "none detected")}",
            CodeSnippets = snippets,
            Facts = facts
        };
    }

    // ─── Domain Rules: business logic, state machines, invariants ──

    private static ContextResponse ResolveDomainRules(ContextQuery query, AgentContext context)
    {
        var module = query.Module;
        var entity = query.EntityName;
        var facts = new Dictionary<string, string>();
        var snippets = new List<string>();

        // Extract from requirements
        var relevant = context.Requirements
            .Where(r => r.Module.Contains(module, StringComparison.OrdinalIgnoreCase) ||
                        r.Title.Contains(entity, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();
        foreach (var req in relevant.Take(10))
        {
            sb.AppendLine($"## {req.Title}");
            sb.AppendLine(req.Description);
            foreach (var ac in req.AcceptanceCriteria)
                sb.AppendLine($"- AC: {ac}");
            sb.AppendLine();
        }

        if (sb.Length > 0)
            facts["Requirements"] = sb.ToString();

        // Extract from expanded requirements (detailed specs)
        var expanded = context.ExpandedRequirements
            .Where(e => e.Module.Contains(module, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(e.DetailedSpec))
            .ToList();

        foreach (var exp in expanded.Take(5))
            facts[$"Spec:{exp.Id}"] = exp.DetailedSpec;

        // Extract from existing service implementations (business logic)
        var svcArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Service &&
                        !a.FileName.StartsWith("I", StringComparison.Ordinal) &&
                        a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var art in svcArtifacts.Take(3))
            snippets.Add(art.Content);

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.RequirementsReader,
            Success = relevant.Count > 0 || expanded.Count > 0,
            Answer = $"Domain rules: {relevant.Count} requirements, {expanded.Count} detailed specs for {module}/{entity}",
            CodeSnippets = snippets,
            Facts = facts
        };
    }

    // ─── Compliance Constraints: HIPAA, SOC2, audit requirements ──

    private static ContextResponse ResolveComplianceConstraints(ContextQuery query, AgentContext context)
    {
        var facts = new Dictionary<string, string>
        {
            ["HIPAA_Required"] = "true",
            ["SOC2_Required"] = "true",
            ["AuditColumns"] = "CreatedAt, CreatedBy, UpdatedAt, UpdatedBy",
            ["TenantIsolation"] = "RLS + EF query filter on TenantId",
            ["EncryptionAtRest"] = "AES-256 for PHI columns",
            ["BreachNotification"] = "72-hour HIPAA notification requirement",
            ["MinimumNecessary"] = "Limit PHI exposure to role-based minimum",
            ["AccessAudit"] = "Log all PHI read/write with user, timestamp, IP"
        };

        // Add any existing compliance findings
        var compFindings = context.Findings
            .Where(f => f.Category.Contains("HIPAA", StringComparison.OrdinalIgnoreCase) ||
                        f.Category.Contains("SOC2", StringComparison.OrdinalIgnoreCase) ||
                        f.Category.Contains("Compliance", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (compFindings.Count > 0)
            facts["OpenComplianceFindings"] = string.Join("; ", compFindings.Take(10).Select(f => $"[{f.Category}] {f.Message}"));

        var compArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Compliance)
            .Select(a => a.Content)
            .Take(3)
            .ToList();

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.HipaaCompliance,
            Success = true,
            Answer = $"Compliance: HIPAA + SOC2 required, {compFindings.Count} open findings",
            CodeSnippets = compArtifacts,
            Facts = facts
        };
    }

    // ─── Implementation Status: what's built, what's missing ──

    private static ContextResponse ResolveImplementationStatus(ContextQuery query, AgentContext context)
    {
        var module = query.Module;
        var facts = new Dictionary<string, string>();

        // Count artifacts per layer for this module
        var moduleArtifacts = context.Artifacts
            .Where(a => a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var byLayer = moduleArtifacts.GroupBy(a => a.Layer)
            .ToDictionary(g => g.Key.ToString(), g => g.Count().ToString());

        foreach (var kv in byLayer)
            facts[$"Artifacts:{kv.Key}"] = kv.Value;

        // Backlog status for this module
        var moduleItems = context.ExpandedRequirements
            .Where(e => e.Module.Contains(module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        facts["BacklogTotal"] = moduleItems.Count.ToString();
        facts["BacklogCompleted"] = moduleItems.Count(i => i.Status == WorkItemStatus.Completed).ToString();
        facts["BacklogInProgress"] = moduleItems.Count(i => i.Status == WorkItemStatus.InProgress).ToString();
        facts["BacklogBlocked"] = moduleItems.Count(i => i.Status == WorkItemStatus.Blocked).ToString();

        // Open findings
        var moduleFindings = context.Findings
            .Where(f => f.FilePath?.Contains(module, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        facts["OpenFindings"] = moduleFindings.Count.ToString();

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Backlog,
            Success = true,
            Answer = $"Module {module}: {moduleArtifacts.Count} artifacts, {moduleItems.Count(i => i.Status == WorkItemStatus.Completed)}/{moduleItems.Count} items complete, {moduleFindings.Count} open findings",
            Facts = facts
        };
    }

    // ─── Architecture Decision: patterns, tech choices, rationale ──

    private static ContextResponse ResolveArchitectureDecision(ContextQuery query, AgentContext context)
    {
        var facts = new Dictionary<string, string>
        {
            ["Pattern"] = "Clean Architecture — Domain → Application → Infrastructure → WebAPI",
            ["Database"] = "PostgreSQL 16 with EF Core, schema-per-service, RLS for tenant isolation",
            ["Messaging"] = "Apache Kafka with transactional outbox pattern, schema registry",
            ["Authentication"] = "JWT Bearer with RBAC + ABAC, break-the-glass emergency access",
            ["API_Style"] = "REST with Minimal APIs, YARP API Gateway reverse proxy",
            ["Observability"] = "OpenTelemetry, Prometheus metrics, Grafana dashboards, structured logging",
            ["Deployment"] = "Docker Compose (dev), Kubernetes + Helm (prod)",
            ["Testing"] = "xUnit + Moq unit tests, integration tests, load tests (k6)"
        };

        // Add architect instructions if present
        var archInstructions = context.OrchestratorInstructions
            .Where(i => i.StartsWith("[ARCH]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (archInstructions.Count > 0)
            facts["ArchitectDirectives"] = string.Join("; ", archInstructions);

        var platformInstructions = context.OrchestratorInstructions
            .Where(i => i.StartsWith("[PLATFORM]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (platformInstructions.Count > 0)
            facts["PlatformDirectives"] = string.Join("; ", platformInstructions);

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Architect,
            Success = true,
            Answer = "Architecture: Clean Architecture, PostgreSQL + Kafka, REST + YARP, Docker/K8s",
            Facts = facts
        };
    }

    // ─── Dependency Info: which services this entity/feature depends on ──

    private static ContextResponse ResolveDependencyInfo(ContextQuery query, AgentContext context)
    {
        var module = query.Module;
        var facts = new Dictionary<string, string>();

        {
            var svcDef = MicroserviceCatalog.ByName(module) ??
                MicroserviceCatalog.All.FirstOrDefault(s => s.Name.Contains(module, StringComparison.OrdinalIgnoreCase));

            if (svcDef is not null)
            {
                facts["ServiceName"] = svcDef.Name;
                facts["DependsOn"] = svcDef.DependsOn.Length > 0 ? string.Join(", ", svcDef.DependsOn) : "none";
                facts["Schema"] = svcDef.Schema;
                facts["Port"] = svcDef.ApiPort.ToString();
            }

            if (context.DomainModel is not null)
            {
                var events = context.DomainModel.DomainEvents
                    .Where(e => e.ServiceName.Contains(module, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                facts["Events"] = events.Count > 0 ? string.Join(", ", events.Select(e => e.EventName)) : "none";
            }

            // Find reverse dependencies (who depends on this service)
            var dependents = MicroserviceCatalog.All
                .Where(s => s.DependsOn.Any(d => d.Contains(module, StringComparison.OrdinalIgnoreCase)))
                .Select(s => s.Name)
                .ToList();

            if (dependents.Count > 0)
                facts["DependedOnBy"] = string.Join(", ", dependents);
        }

        // Check backlog dependency chains
        var moduleItems = context.ExpandedRequirements
            .Where(e => e.Module.Contains(module, StringComparison.OrdinalIgnoreCase) &&
                        e.ResolvedDependencyChain.Count > 0)
            .Take(5)
            .ToList();

        if (moduleItems.Count > 0)
            facts["DependencyChainSample"] = string.Join("; ",
                moduleItems.Select(i => $"{i.Id} → [{string.Join(",", i.ResolvedDependencyChain.Take(3))}]"));

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Architect,
            Success = facts.Count > 0,
            Answer = $"Dependencies for {module}: depends on [{facts.GetValueOrDefault("DependsOn", "unknown")}], " +
                     $"depended on by [{facts.GetValueOrDefault("DependedOnBy", "none")}]",
            Facts = facts
        };
    }

    // ─── Test Strategy: what tests are needed, coverage expectations ──

    private static ContextResponse ResolveTestStrategy(ContextQuery query, AgentContext context)
    {
        var module = query.Module;
        var entity = query.EntityName;
        var facts = new Dictionary<string, string>
        {
            ["UnitTestFramework"] = "xUnit + Moq",
            ["IntegrationTestPattern"] = "WebApplicationFactory with InMemoryDb",
            ["CoverageTarget"] = "80% line coverage minimum",
            ["RequiredTestTypes"] = "Unit (service logic), Repository (EF InMemory), Tenant isolation, Authorization, Validation, Integration (Kafka events)"
        };

        // Check existing test artifacts for this module/entity
        var testArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Test &&
                        (a.Namespace.Contains(module, StringComparison.OrdinalIgnoreCase) ||
                         a.FileName.Contains(entity, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        facts["ExistingTestCount"] = testArtifacts.Count.ToString();

        // Check test diagnostics
        var testDiags = context.TestDiagnostics
            .Where(d => d.AgentUnderTest.Contains(module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        facts["TestDiagnostics"] = testDiags.Count.ToString();
        facts["PassedTests"] = testDiags.Count(d => d.Outcome == TestOutcome.Passed).ToString();
        facts["FailedTests"] = testDiags.Count(d => d.Outcome == TestOutcome.Failed).ToString();

        var snippets = testArtifacts.Take(2).Select(a => a.Content).ToList();

        return new ContextResponse
        {
            QueryId = query.Id,
            RespondedBy = AgentType.Testing,
            Success = true,
            Answer = $"Test strategy: xUnit+Moq, {testArtifacts.Count} existing tests, target 80% coverage",
            CodeSnippets = snippets,
            Facts = facts
        };
    }
}
