using System.Diagnostics;
using System.Text;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.CodeReasoning;

/// <summary>
/// Post-generation reasoning agent that studies all generated artifacts holistically
/// to detect cross-service inconsistencies, mismatched contracts, missing wiring,
/// and data-flow gaps that individual code-gen agents cannot see alone.
///
/// Runs AFTER all code-gen agents (Database, ServiceLayer, Application, Integration)
/// but BEFORE Review, so findings can guide the Review agent and trigger BugFix cycles.
///
/// Checks:
///   1. Entity/DTO contract alignment — DB entity fields match DTO properties
///   2. Service → Repository wiring — every service interface has a repository call
///   3. Event producer/consumer pairing — published events have matching consumers
///   4. Multi-tenant consistency — all entities have TenantId + RLS
///   5. Naming convention coherence — Pascal/camelCase, namespace consistency
///   6. Cross-service dependency correctness — consumed events match published schemas
///   7. Compliance wiring — Sensitive entities have audit + encryption attributes
/// </summary>
public sealed class CodeReasoningAgent : IAgent
{
    private readonly IContextBroker _broker;
    private readonly ILogger<CodeReasoningAgent> _logger;

    public AgentType Type => AgentType.CodeReasoning;
    public string Name => "Code Reasoning Agent";
    public string Description => "Holistic post-generation analysis that detects cross-service inconsistencies, contract mismatches, missing wiring, and data-flow gaps before Review.";

    public CodeReasoningAgent(IContextBroker broker, ILogger<CodeReasoningAgent> logger)
    {
        _broker = broker;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        context.ReportProgress?.Invoke(Type,
            $"Reasoning over {context.Artifacts.Count} artifacts across {ServiceCatalogResolver.GetServices(context).Count} services...");

        try
        {
        var findings = new List<ReviewFinding>();
        var messages = new List<AgentMessage>();

        // ─── Phase 1: Entity ↔ DTO contract alignment ───
        var contractFindings = await CheckContractAlignment(context, ct);
        findings.AddRange(contractFindings);

        // ─── Phase 2: Service → Repository wiring ───
        var wiringFindings = CheckServiceRepositoryWiring(context);
        findings.AddRange(wiringFindings);

        // ─── Phase 3: Event producer/consumer pairing ───
        var eventFindings = await CheckEventPairing(context, ct);
        findings.AddRange(eventFindings);

        // ─── Phase 4: Multi-tenant consistency ───
        var tenantFindings = CheckMultiTenantConsistency(context);
        findings.AddRange(tenantFindings);

        // ─── Phase 5: Naming convention coherence ───
        var namingFindings = CheckNamingConventions(context);
        findings.AddRange(namingFindings);

        // ─── Phase 6: Cross-service dependency correctness ───
        var depFindings = await CheckCrossServiceDependencies(context, ct);
        findings.AddRange(depFindings);

        // ─── Phase 7: Compliance wiring ───
        var complianceFindings = await CheckComplianceWiring(context, ct);
        findings.AddRange(complianceFindings);

        // Publish findings
        foreach (var f in findings)
            context.Findings.Add(f);

        // Send summary directive to Review agent so it knows what's already been flagged
        if (findings.Count > 0)
        {
            context.DirectiveQueue.Enqueue(new AgentDirective
            {
                From = Type,
                To = AgentType.Review,
                Action = "REASONING_FINDINGS",
                Details = $"CodeReasoning found {findings.Count} issues: " +
                          string.Join("; ", findings.GroupBy(f => f.Category)
                              .Select(g => $"{g.Key}={g.Count()}")),
                Priority = 1
            });
        }

        context.AgentStatuses[Type] = AgentStatus.Completed;

        var result = new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Reasoned over {context.Artifacts.Count} artifacts — found {findings.Count} cross-service issues " +
                      $"({findings.Count(f => f.Severity == ReviewSeverity.Critical)} critical, " +
                      $"{findings.Count(f => f.Severity == ReviewSeverity.Error)} errors, " +
                      $"{findings.Count(f => f.Severity == ReviewSeverity.Warning)} warnings)",
            Messages = messages,
            Duration = sw.Elapsed
        };

        _logger.LogInformation("[CodeReasoning] Completed in {Elapsed}ms — {FindingCount} findings",
            sw.ElapsedMilliseconds, findings.Count);

        return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CodeReasoningAgent failed — {ExType}: {Message}", ex.GetType().Name, ex.Message);
            context.AgentStatuses[Type] = AgentStatus.Failed;
            return new AgentResult
            {
                Agent = Type, Success = false,
                Errors = [ex.ToString()],
                Summary = $"CodeReasoning failed: {ex.GetType().Name}: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }

    // ─── Phase 1: Entity ↔ DTO contract alignment ───────────────────────────

    private async Task<List<ReviewFinding>> CheckContractAlignment(AgentContext context, CancellationToken ct)
    {
        var findings = new List<ReviewFinding>();

        if (context.DomainModel is null) return findings;

        // Group entities by service
        var entitiesByService = context.DomainModel.Entities
            .GroupBy(e => e.ServiceName, StringComparer.OrdinalIgnoreCase);

        foreach (var svcGroup in entitiesByService)
        {
            foreach (var entity in svcGroup)
            {
                // Get entity schema via broker
                var schemaResponse = await _broker.ResolveAsync(new ContextQuery
                {
                    From = Type, To = AgentType.Database,
                    Intent = QueryIntent.EntitySchema,
                    Module = svcGroup.Key, EntityName = entity.Name
                }, context, ct);

                // Get API contract via broker
                var apiResponse = await _broker.ResolveAsync(new ContextQuery
                {
                    From = Type, To = AgentType.ServiceLayer,
                    Intent = QueryIntent.ApiContract,
                    Module = svcGroup.Key, EntityName = entity.Name
                }, context, ct);

                if (!schemaResponse.Success || !apiResponse.Success) continue;

                // Compare entity fields with DTO fields
                var entityFields = schemaResponse.Facts.Keys
                    .Select(k => k.ToLowerInvariant()).ToHashSet();
                var dtoFields = apiResponse.Facts.Keys
                    .Select(k => k.ToLowerInvariant()).ToHashSet();

                // Fields in entity but missing from DTO (potential data leak or missing mapping)
                var entityOnly = entityFields.Except(dtoFields)
                    .Where(f => f is not "tenantid" and not "createdat" and not "createdby"
                        and not "updatedat" and not "updatedby" and not "isdeleted"
                        and not "rowversion")
                    .ToList();

                // Fields in DTO but missing from entity (phantom fields)
                var dtoOnly = dtoFields.Except(entityFields)
                    .Where(f => f is not "id" and not "links" and not "href")
                    .ToList();

                if (entityOnly.Count > 0)
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "ContractAlignment",
                        Severity = ReviewSeverity.Warning,
                        Message = $"{svcGroup.Key}/{entity.Name}: Entity has fields not in DTO: {string.Join(", ", entityOnly)}. Potential missing mapping or intentional exclusion.",
                        FilePath = $"src/GNex.{svcGroup.Key}/DTOs/{entity.Name}Dto.cs",
                        Suggestion = "Add missing fields to DTO or verify intentional exclusion (e.g., internal audit fields)."
                    });
                }

                if (dtoOnly.Count > 0)
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "ContractAlignment",
                        Severity = ReviewSeverity.Error,
                        Message = $"{svcGroup.Key}/{entity.Name}: DTO has fields not in entity: {string.Join(", ", dtoOnly)}. Phantom fields with no backing.",
                        FilePath = $"src/GNex.{svcGroup.Key}/DTOs/{entity.Name}Dto.cs",
                        Suggestion = "Remove phantom DTO fields or add corresponding entity properties."
                    });
                }
            }
        }

        return findings;
    }

    // ─── Phase 2: Service → Repository wiring ───────────────────────────────

    private static List<ReviewFinding> CheckServiceRepositoryWiring(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        // Get all service interface artifacts
        var serviceInterfaces = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Service &&
                        a.FileName.StartsWith("I") && a.FileName.EndsWith("Service.cs"))
            .ToList();

        // Get all repository artifacts
        var repos = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Repository &&
                        a.FileName.EndsWith("Repository.cs"))
            .Select(a => a.FileName.Replace("Repository.cs", "").Replace("I", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var svcArtifact in serviceInterfaces)
        {
            var entityName = svcArtifact.FileName
                .Replace("I", "").Replace("Service.cs", "");

            if (!repos.Contains(entityName))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Implementation",
                    Severity = ReviewSeverity.Error,
                    Message = $"Service interface {svcArtifact.FileName} has no matching repository: I{entityName}Repository / {entityName}Repository",
                    FilePath = svcArtifact.RelativePath,
                    Suggestion = $"Generate {entityName}Repository implementing I{entityName}Repository."
                });
            }
        }

        // Check the reverse: repositories without services
        var serviceEntities = serviceInterfaces
            .Select(a => a.FileName.Replace("I", "").Replace("Service.cs", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repoArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Repository && a.FileName.EndsWith("Repository.cs"))
            .ToList();

        foreach (var repoArtifact in repoArtifacts)
        {
            var entityName = repoArtifact.FileName
                .Replace("Repository.cs", "").Replace("I", "");

            if (!serviceEntities.Contains(entityName) && !entityName.Equals("Base", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Implementation",
                    Severity = ReviewSeverity.Warning,
                    Message = $"Repository {repoArtifact.FileName} has no matching service interface — orphaned repository.",
                    FilePath = repoArtifact.RelativePath,
                    Suggestion = $"Create I{entityName}Service and {entityName}Service, or remove if entity is not exposed via API."
                });
            }
        }

        return findings;
    }

    // ─── Phase 3: Event producer/consumer pairing ───────────────────────────

    private async Task<List<ReviewFinding>> CheckEventPairing(AgentContext context, CancellationToken ct)
    {
        var findings = new List<ReviewFinding>();

        if (context.DomainModel is null) return findings;

        // Build maps of published and consumed events from DomainEvents
        var published = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var consumed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in context.DomainModel.DomainEvents)
        {
            if (evt.EventType.Contains("Publish", StringComparison.OrdinalIgnoreCase) ||
                evt.EventType.Contains("Created", StringComparison.OrdinalIgnoreCase) ||
                evt.EventType.Contains("Updated", StringComparison.OrdinalIgnoreCase))
            {
                published[evt.EventName] = evt.ServiceName;
            }
            else if (evt.EventType.Contains("Consum", StringComparison.OrdinalIgnoreCase))
            {
                if (!consumed.TryGetValue(evt.EventName, out var consumers))
                {
                    consumers = [];
                    consumed[evt.EventName] = consumers;
                }
                consumers.Add(evt.ServiceName);
            }
        }

        // Published events with no consumers → dead events
        foreach (var (evt, publisher) in published)
        {
            if (!consumed.ContainsKey(evt))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Integration",
                    Severity = ReviewSeverity.Warning,
                    Message = $"Event '{evt}' published by {publisher} has no consumers — dead event.",
                    FilePath = $"src/GNex.{publisher}/Events/{evt}.cs",
                    Suggestion = "Add consumer in dependent service or remove event publication if not needed."
                });
            }
        }

        // Consumed events with no publisher → dangling consumer
        foreach (var (evt, consumers) in consumed)
        {
            if (!published.ContainsKey(evt))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Integration",
                    Severity = ReviewSeverity.Critical,
                    Message = $"Event '{evt}' consumed by {string.Join(", ", consumers)} is never published — consumer will hang.",
                    FilePath = $"src/Integration/Consumers/{evt}Consumer.cs",
                    Suggestion = "Add event publication in the owning service or remove dangling consumer."
                });
            }
        }

        // Also check integration artifacts per service for consumer implementations
        var consumingServices = consumed.Values.SelectMany(c => c).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var svcName in consumingServices)
        {
            var eventsConsumedByService = consumed
                .Where(kvp => kvp.Value.Contains(svcName, StringComparer.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            var intResponse = await _broker.ResolveAsync(new ContextQuery
            {
                From = Type, To = AgentType.Integration,
                Intent = QueryIntent.IntegrationContract,
                Module = svcName
            }, context, ct);

            // Check if artifacts contain actual consumer implementations
            var consumerArtifacts = context.Artifacts
                .Where(a => a.Layer == ArtifactLayer.Integration &&
                            a.Content.Contains(svcName, StringComparison.OrdinalIgnoreCase) &&
                            a.Content.Contains("Consumer", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (consumerArtifacts.Count == 0 && eventsConsumedByService.Count > 0)
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Integration",
                    Severity = ReviewSeverity.Error,
                    Message = $"{svcName} declares {eventsConsumedByService.Count} consumed events but no consumer artifacts were generated.",
                    FilePath = $"src/GNex.{svcName}/Consumers/",
                    Suggestion = "Generate Kafka consumer classes for consumed events with manual-commit + idempotency."
                });
            }
        }

        return findings;
    }

    // ─── Phase 4: Multi-tenant consistency ──────────────────────────────────

    private static List<ReviewFinding> CheckMultiTenantConsistency(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        // Check entity artifacts for TenantId
        var entityArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Database && a.FileName.EndsWith(".cs") && !a.FileName.Contains("DbContext") && !a.FileName.Contains("Repository"))
            .ToList();

        foreach (var artifact in entityArtifacts)
        {
            if (!artifact.Content.Contains("TenantId", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "MultiTenant",
                    Severity = ReviewSeverity.Critical,
                    Message = $"Entity {artifact.FileName} is missing TenantId property — breaks multi-tenant isolation.",
                    FilePath = artifact.RelativePath,
                    Suggestion = "Add: public Guid TenantId { get; set; } — required for RLS and query filters."
                });
            }
        }

        // Check DbContext artifacts for query filters
        var dbContextArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Database && a.FileName.Contains("DbContext"))
            .ToList();

        foreach (var artifact in dbContextArtifacts)
        {
            if (!artifact.Content.Contains("HasQueryFilter", StringComparison.OrdinalIgnoreCase) &&
                !artifact.Content.Contains("QueryFilter", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "MultiTenant",
                    Severity = ReviewSeverity.Critical,
                    Message = $"DbContext {artifact.FileName} is missing tenant query filters — data leak across tenants.",
                    FilePath = artifact.RelativePath,
                    Suggestion = "Add .HasQueryFilter(e => e.TenantId == _tenantId) for every entity in OnModelCreating."
                });
            }
        }

        // Check migration scripts for RLS policies
        var migrationArtifacts = context.Artifacts
            .Where(a => a.Layer == ArtifactLayer.Migration && a.FileName.Contains("Migration"))
            .ToList();

        var hasRls = context.Artifacts
            .Any(a => a.Content.Contains("CREATE POLICY", StringComparison.OrdinalIgnoreCase) ||
                      a.Content.Contains("ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));

        if (entityArtifacts.Count > 0 && !hasRls)
        {
            findings.Add(new ReviewFinding
            {
                Category = "MultiTenant",
                Severity = ReviewSeverity.Error,
                Message = "No RLS policies detected — PostgreSQL row-level security is required for defense-in-depth tenant isolation.",
                FilePath = "Infrastructure/Migrations/",
                Suggestion = "Generate ALTER TABLE ... ENABLE ROW LEVEL SECURITY + CREATE POLICY for each entity table."
            });
        }

        return findings;
    }

    // ─── Phase 5: Naming convention coherence ───────────────────────────────

    private static List<ReviewFinding> CheckNamingConventions(AgentContext context)
    {
        var findings = new List<ReviewFinding>();

        // Check namespace consistency
        var namespaceGroups = context.Artifacts
            .Where(a => !string.IsNullOrEmpty(a.Namespace))
            .GroupBy(a => a.Namespace.Split('.').FirstOrDefault() ?? "Unknown")
            .ToList();

        // All namespaces should start with "GNex"
        foreach (var group in namespaceGroups)
        {
            if (!group.Key.StartsWith("GNex", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Conventions",
                    Severity = ReviewSeverity.Warning,
                    Message = $"Namespace root '{group.Key}' doesn't follow project convention — expected 'GNex.*'. Affects {group.Count()} artifacts.",
                    FilePath = group.First().RelativePath,
                    Suggestion = "Change namespace root to 'GNex.{ServiceName}.{Layer}'."
                });
            }
        }

        // Check file naming
        foreach (var artifact in context.Artifacts.Where(a => a.FileName.EndsWith(".cs")))
        {
            // Interfaces should start with I
            if (artifact.Content.Contains("interface ") && !artifact.FileName.StartsWith("I"))
            {
                findings.Add(new ReviewFinding
                {
                    Category = "Conventions",
                    Severity = ReviewSeverity.Warning,
                    Message = $"Interface file {artifact.FileName} doesn't start with 'I' — violates C# naming conventions.",
                    FilePath = artifact.RelativePath,
                    Suggestion = $"Rename to I{artifact.FileName}."
                });
            }
        }

        return findings;
    }

    // ─── Phase 6: Cross-service dependency correctness ──────────────────────

    private async Task<List<ReviewFinding>> CheckCrossServiceDependencies(AgentContext context, CancellationToken ct)
    {
        var findings = new List<ReviewFinding>();
        var catalog = ServiceCatalogResolver.GetServices(context);

        foreach (var svcDef in catalog)
        {
            foreach (var dep in svcDef.DependsOn)
            {
                // Check if the dependency service actually exists
                var depService = ServiceCatalogResolver.ByName(context, dep);

                if (depService is null)
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "ArchitectureViolation",
                        Severity = ReviewSeverity.Critical,
                        Message = $"{svcDef.Name} depends on '{dep}' which doesn't exist in the service catalog.",
                        FilePath = $"src/GNex.{svcDef.Name}/",
                        Suggestion = $"Add '{dep}' service to the service catalog or remove the dependency from {svcDef.Name}."
                    });
                    continue;
                }

                // Query the dependency's contracts via context broker
                var depResponse = await _broker.ResolveAsync(new ContextQuery
                {
                    From = Type, To = AgentType.Integration,
                    Intent = QueryIntent.DependencyInfo,
                    Module = dep
                }, context, ct);

                // Check for circular dependencies
                if (depService.DependsOn.Contains(svcDef.Name))
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "ArchitectureViolation",
                        Severity = ReviewSeverity.Critical,
                        Message = $"Circular dependency detected: {svcDef.Name} ↔ {dep}. This creates tight coupling and deployment ordering issues.",
                        FilePath = $"src/GNex.{svcDef.Name}/",
                        Suggestion = "Break circular dependency using event-driven communication (Kafka) or introduce an intermediary service."
                    });
                }
            }
        }

        return findings;
    }

    // ─── Phase 7: Compliance wiring ─────────────────────────────────────────

    private async Task<List<ReviewFinding>> CheckComplianceWiring(AgentContext context, CancellationToken ct)
    {
        var findings = new List<ReviewFinding>();

        if (context.DomainModel is null) return findings;

        // Identify sensitive entities by checking if they have a ClassificationCode field
        // or contain common sensitive data keywords
        var sensitiveKeywords = new[] { "personal", "private", "secret", "credential", "payment", "financial", "identity", "address", "phone", "email", "ssn", "birth" };

        foreach (var entity in context.DomainModel.Entities)
        {
            var isSensitive = sensitiveKeywords.Any(k =>
                entity.Name.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                entity.Fields.Any(f => f.Name.Contains("Classification", StringComparison.OrdinalIgnoreCase));

            if (!isSensitive) continue;

            // Query compliance requirements via broker
            var compResponse = await _broker.ResolveAsync(new ContextQuery
            {
                From = Type, To = AgentType.Soc2Compliance,
                Intent = QueryIntent.ComplianceConstraints,
                Module = entity.ServiceName, EntityName = entity.Name
            }, context, ct);

            // Check entity artifact for audit columns
            var entityArtifact = context.Artifacts
                .FirstOrDefault(a => a.Layer == ArtifactLayer.Database &&
                                     a.FileName.Contains(entity.Name, StringComparison.OrdinalIgnoreCase));

            if (entityArtifact is not null)
            {
                var content = entityArtifact.Content;

                // Sensitive entities must have audit columns
                if (!content.Contains("CreatedAt") || !content.Contains("UpdatedAt"))
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "DATA-GOV-01",
                        Severity = ReviewSeverity.Critical,
                        Message = $"Sensitive entity {entity.Name} in {entity.ServiceName} is missing audit trail columns (CreatedAt/UpdatedAt).",
                        FilePath = entityArtifact.RelativePath,
                        Suggestion = "Add CreatedAt, CreatedBy, UpdatedAt, UpdatedBy properties for audit compliance."
                    });
                }

                // Sensitive entities should have data classification attribute
                if (!content.Contains("DataClassification") && !content.Contains("[Restricted]") && !content.Contains("[Confidential]"))
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "Security",
                        Severity = ReviewSeverity.Warning,
                        Message = $"Sensitive entity {entity.Name} in {entity.ServiceName} lacks data classification attribute.",
                        FilePath = entityArtifact.RelativePath,
                        Suggestion = "Add [DataClassification(\"Restricted\")] or [Confidential] attribute for sensitive data."
                    });
                }
            }

            // Check that service layer enforces minimum-necessary access
            var serviceArtifact = context.Artifacts
                .FirstOrDefault(a => a.Layer == ArtifactLayer.Service &&
                                     a.FileName.Contains(entity.Name, StringComparison.OrdinalIgnoreCase) &&
                                     a.FileName.EndsWith("Service.cs"));

            if (serviceArtifact is not null)
            {
                if (!serviceArtifact.Content.Contains("Authorize", StringComparison.OrdinalIgnoreCase) &&
                    !serviceArtifact.Content.Contains("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new ReviewFinding
                    {
                        Category = "DATA-GOV-02",
                        Severity = ReviewSeverity.Error,
                        Message = $"Sensitive service {serviceArtifact.FileName} has no authorization check — minimum-necessary access rule violated.",
                        FilePath = serviceArtifact.RelativePath,
                        Suggestion = "Add role-based authorization (e.g., [Authorize(Policy = \"CanAccessSensitiveData\")]) to all sensitive data endpoints."
                    });
                }
            }
        }

        return findings;
    }
}
