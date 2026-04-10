using System.Diagnostics;
using System.Text;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Planning;

/// <summary>
/// Reasoning agent that creates structured implementation plans before code generation.
/// Analyzes requirements, domain model, existing artifacts, and inter-service dependencies
/// to produce a coherent execution plan that downstream agents can follow.
///
/// This is the "thinking" layer of the agent ecosystem — it answers:
///   1. What entities/services need to change?
///   2. What is the correct order of implementation?
///   3. What cross-cutting concerns apply (security, compliance, integration)?
///   4. What existing code must be preserved or extended?
///   5. What standards and patterns must be followed?
///
/// The plan is stored as structured <see cref="ImplementationPlan"/> in <see cref="AgentContext"/>.
/// Code-gen agents read the plan to make context-aware decisions.
/// </summary>
public sealed class PlanningAgent : IAgent
{
    private readonly IContextBroker _broker;
    private readonly ILlmProvider _llm;
    private readonly ILogger<PlanningAgent> _logger;

    public AgentType Type => AgentType.Planning;
    public string Name => "Planning & Reasoning Agent";
    public string Description => "Analyzes requirements holistically and creates structured implementation plans for code-gen agents to follow, ensuring coordinated, context-aware development.";

    public PlanningAgent(IContextBroker broker, ILlmProvider llm, ILogger<PlanningAgent> logger)
    {
        _broker = broker;
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        try
        {
        var plan = new ImplementationPlan { RunId = context.RunId, Iteration = context.DevIteration };

        context.ReportProgress?.Invoke(Type,
            $"Reasoning about {context.Requirements.Count} requirements and {context.ExpandedRequirements.Count} backlog items...");

        // ─── Phase 1: Identify affected services and entities ───
        var affectedServices = IdentifyAffectedServices(context);
        plan.AffectedServices = affectedServices;

        context.ReportProgress?.Invoke(Type,
            $"Identified {affectedServices.Count} affected services: {string.Join(", ", affectedServices.Select(s => s.ServiceName))}");

        // ─── Phase 2: Query context broker for each service's current state ───
        foreach (var svc in affectedServices)
        {
            ct.ThrowIfCancellationRequested();

            // Query entity schemas from Database agent's output
            foreach (var entity in svc.Entities)
            {
                var schemaResponse = await _broker.ResolveAsync(new ContextQuery
                {
                    From = Type, To = AgentType.Database,
                    Intent = QueryIntent.EntitySchema,
                    Module = svc.ServiceName, EntityName = entity
                }, context, ct);

                if (schemaResponse.Success)
                    svc.EntitySchemas[entity] = schemaResponse.Facts;
            }

            // Query API contracts from ServiceLayer's output
            var apiResponse = await _broker.ResolveAsync(new ContextQuery
            {
                From = Type, To = AgentType.ServiceLayer,
                Intent = QueryIntent.ApiContract,
                Module = svc.ServiceName
            }, context, ct);

            if (apiResponse.Success)
                svc.ApiContracts = apiResponse.Facts;

            // Query integration dependencies
            var intResponse = await _broker.ResolveAsync(new ContextQuery
            {
                From = Type, To = AgentType.Integration,
                Intent = QueryIntent.IntegrationContract,
                Module = svc.ServiceName
            }, context, ct);

            if (intResponse.Success)
                svc.IntegrationContracts = intResponse.Facts;

            // Query compliance constraints
            var compResponse = await _broker.ResolveAsync(new ContextQuery
            {
                From = Type, To = AgentType.HipaaCompliance,
                Intent = QueryIntent.ComplianceConstraints,
                Module = svc.ServiceName
            }, context, ct);

            if (compResponse.Success)
                svc.ComplianceConstraints = compResponse.Facts;
        }

        // ─── Phase 3: Determine implementation order using dependency analysis ───
        plan.ExecutionOrder = DetermineExecutionOrder(affectedServices, context);

        context.ReportProgress?.Invoke(Type,
            $"Execution order: {string.Join(" → ", plan.ExecutionOrder.Select(o => $"{o.ServiceName}({o.Layer})"))}");

        // ─── Phase 4: Identify cross-cutting concerns ───
        plan.CrossCuttingConcerns = IdentifyCrossCuttingConcerns(context, affectedServices);

        // ─── Phase 5: Create agent-specific instructions ───
        plan.AgentInstructions = CreateAgentInstructions(context, affectedServices, plan);

        // ─── Phase 6: Identify standards and patterns that must be followed ───
        plan.Standards = IdentifyStandards(context);

        // ─── Phase 7: LLM-enhanced plan review ───
        await EnhancePlanWithLlmAsync(plan, context, ct);

        // ─── Phase 8: Store the plan in context for downstream agents ───
        context.ImplementationPlan = plan;

        // Also publish as an orchestrator instruction for agents to read
        var planSummary = FormatPlanSummary(plan);
        context.OrchestratorInstructions.Add($"[PLAN] {planSummary}");

        // Send directives to each code-gen agent with their specific instructions
        foreach (var (agentType, instruction) in plan.AgentInstructions)
        {
            context.DirectiveQueue.Enqueue(new AgentDirective
            {
                From = Type,
                To = agentType,
                Action = "IMPLEMENT_PLAN",
                Details = instruction,
                Priority = 1
            });
        }

        context.AgentStatuses[Type] = AgentStatus.Completed;

        var result = new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Plan created: {affectedServices.Count} services, {plan.ExecutionOrder.Count} steps, " +
                      $"{plan.CrossCuttingConcerns.Count} cross-cutting concerns, " +
                      $"{plan.AgentInstructions.Count} agent instructions",
            Duration = sw.Elapsed
        };

        _logger.LogInformation("[Planning] Plan created in {Elapsed}ms — {ServiceCount} services, {StepCount} execution steps",
            sw.ElapsedMilliseconds, affectedServices.Count, plan.ExecutionOrder.Count);

        return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlanningAgent failed — {ExType}: {Message}", ex.GetType().Name, ex.Message);
            context.AgentStatuses[Type] = AgentStatus.Failed;
            return new AgentResult
            {
                Agent = Type, Success = false,
                Errors = [ex.ToString()],
                Summary = $"PlanningAgent failed: {ex.GetType().Name}: {ex.Message}",
                Duration = sw.Elapsed
            };
        }
    }

    // ─── Identify which services are affected by the current requirements ───

    private static List<ServicePlan> IdentifyAffectedServices(AgentContext context)
    {
        var services = new Dictionary<string, ServicePlan>(StringComparer.OrdinalIgnoreCase);

        // From microservice catalog + domain model entities
        foreach (var svcDef in MicroserviceCatalog.All)
        {
            var entities = context.DomainModel?.Entities
                .Where(e => e.ServiceName.Contains(svcDef.ShortName, StringComparison.OrdinalIgnoreCase) ||
                            e.ServiceName.Contains(svcDef.Name, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Name)
                .ToList() ?? [.. svcDef.Entities];

            var events = context.DomainModel?.DomainEvents
                .Where(e => e.ServiceName.Contains(svcDef.Name, StringComparison.OrdinalIgnoreCase))
                .ToList() ?? [];

            var plan = new ServicePlan
            {
                ServiceName = svcDef.Name,
                Schema = svcDef.Schema,
                Port = svcDef.ApiPort,
                Entities = entities.Count > 0 ? entities : [.. svcDef.Entities],
                DependsOn = [.. svcDef.DependsOn],
                PublishedEvents = [.. events.Where(e => e.EventType == "Published").Select(e => e.EventName)],
                ConsumedEvents = [.. events.Where(e => e.EventType == "Consumed").Select(e => e.EventName)]
            };
            services[svcDef.Name] = plan;
        }

        // From expanded requirements — determine which modules have active work
        var activeModules = context.ExpandedRequirements
            .Where(e => e.Status is WorkItemStatus.New or WorkItemStatus.InQueue or WorkItemStatus.InProgress)
            .Select(e => e.Module)
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Mark services as having active work
        foreach (var mod in activeModules)
        {
            var matchKey = services.Keys.FirstOrDefault(k =>
                k.Contains(mod, StringComparison.OrdinalIgnoreCase) ||
                mod.Contains(k.Replace("Service", ""), StringComparison.OrdinalIgnoreCase));

            if (matchKey is not null)
                services[matchKey].HasActiveWork = true;
        }

        return [.. services.Values];
    }

    // ─── Determine the correct implementation order based on dependencies ───

    private static List<ExecutionStep> DetermineExecutionOrder(
        List<ServicePlan> services, AgentContext context)
    {
        var steps = new List<ExecutionStep>();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sort by dependency depth (services with no deps first)
        var sorted = TopologicalSort(services);

        foreach (var svc in sorted)
        {
            // Database layer first
            steps.Add(new ExecutionStep
            {
                ServiceName = svc.ServiceName,
                Layer = "Database",
                AgentType = AgentType.Database,
                DependsOnSteps = [.. svc.DependsOn.Where(d => completed.Contains(d)).Select(d => $"{d}:Database")]
            });

            // Service layer after database
            steps.Add(new ExecutionStep
            {
                ServiceName = svc.ServiceName,
                Layer = "Service",
                AgentType = AgentType.ServiceLayer,
                DependsOnSteps = [$"{svc.ServiceName}:Database"]
            });

            // Application layer after service
            steps.Add(new ExecutionStep
            {
                ServiceName = svc.ServiceName,
                Layer = "Application",
                AgentType = AgentType.Application,
                DependsOnSteps = [$"{svc.ServiceName}:Service"]
            });

            // Integration after application
            if (svc.PublishedEvents.Count > 0 || svc.ConsumedEvents.Count > 0)
            {
                steps.Add(new ExecutionStep
                {
                    ServiceName = svc.ServiceName,
                    Layer = "Integration",
                    AgentType = AgentType.Integration,
                    DependsOnSteps = [$"{svc.ServiceName}:Application"]
                });
            }

            // Testing after all layers
            steps.Add(new ExecutionStep
            {
                ServiceName = svc.ServiceName,
                Layer = "Testing",
                AgentType = AgentType.Testing,
                DependsOnSteps = [$"{svc.ServiceName}:Service", $"{svc.ServiceName}:Database"]
            });

            completed.Add(svc.ServiceName);
        }

        return steps;
    }

    private static List<ServicePlan> TopologicalSort(List<ServicePlan> services)
    {
        var byName = services.ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ServicePlan>();

        void Visit(ServicePlan svc)
        {
            if (!visited.Add(svc.ServiceName)) return;
            foreach (var dep in svc.DependsOn)
            {
                if (byName.TryGetValue(dep, out var depSvc))
                    Visit(depSvc);
            }
            result.Add(svc);
        }

        foreach (var svc in services)
            Visit(svc);

        return result;
    }

    // ─── Identify cross-cutting concerns ───

    private static List<CrossCuttingConcern> IdentifyCrossCuttingConcerns(
        AgentContext context, List<ServicePlan> services)
    {
        var concerns = new List<CrossCuttingConcern>
        {
            new()
            {
                Name = "Multi-Tenant Isolation",
                Description = "Every entity must include TenantId. DbContext must apply query filters. RLS policies required in PostgreSQL.",
                AffectsLayers = ["Database", "Service", "Testing"],
                EnforcedBy = [AgentType.Database, AgentType.Review, AgentType.Security],
                Priority = 1
            },
            new()
            {
                Name = "PHI Protection (HIPAA §164.312)",
                Description = "Protected Health Information must be encrypted at rest, audit-logged on access, and subject to minimum-necessary access rules.",
                AffectsLayers = ["Database", "Service", "Application", "Integration"],
                EnforcedBy = [AgentType.HipaaCompliance, AgentType.Security, AgentType.AccessControl],
                Priority = 1
            },
            new()
            {
                Name = "Audit Trail",
                Description = "All entities require CreatedAt, CreatedBy, UpdatedAt, UpdatedBy columns. State changes must be logged for compliance.",
                AffectsLayers = ["Database", "Service"],
                EnforcedBy = [AgentType.Database, AgentType.Review],
                Priority = 1
            },
            new()
            {
                Name = "Event-Driven Integration",
                Description = "Inter-service communication via Kafka with transactional outbox. Events use schema: {EntityName}Created, {EntityName}Updated.",
                AffectsLayers = ["Service", "Integration"],
                EnforcedBy = [AgentType.Integration, AgentType.Review],
                Priority = 2
            },
            new()
            {
                Name = "Observability",
                Description = "OpenTelemetry traces, Prometheus metrics, structured logging with correlation IDs across all services.",
                AffectsLayers = ["Application", "Service", "Integration"],
                EnforcedBy = [AgentType.Observability],
                Priority = 2
            },
            new()
            {
                Name = "Error Handling & Resilience",
                Description = "Global exception middleware, retry policies for Kafka/HTTP, circuit breakers for inter-service calls, dead-letter queues.",
                AffectsLayers = ["Application", "Integration", "Service"],
                EnforcedBy = [AgentType.Performance, AgentType.Review],
                Priority = 2
            }
        };

        // Add data-classification concern if PHI fields detected
        if (services.Any(s => s.Entities.Any(e =>
            e.Contains("Patient", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("Encounter", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("Diagnosis", StringComparison.OrdinalIgnoreCase))))
        {
            concerns.Add(new CrossCuttingConcern
            {
                Name = "Data Classification",
                Description = "Entities containing PHI must be classified (Public/Internal/Confidential/Restricted). API responses must redact Restricted fields per role.",
                AffectsLayers = ["Database", "Service", "Application"],
                EnforcedBy = [AgentType.Security, AgentType.HipaaCompliance],
                Priority = 1
            });
        }

        return concerns;
    }

    // ─── Create specific instructions for each downstream agent ───

    private static Dictionary<AgentType, string> CreateAgentInstructions(
        AgentContext context, List<ServicePlan> services, ImplementationPlan plan)
    {
        var instructions = new Dictionary<AgentType, string>();
        var serviceList = string.Join(", ", services.Select(s => s.ServiceName));

        instructions[AgentType.Database] = FormatDbInstructions(services, plan);
        instructions[AgentType.ServiceLayer] = FormatServiceInstructions(services, plan);
        instructions[AgentType.Application] = FormatAppInstructions(services, plan);
        instructions[AgentType.Integration] = FormatIntegrationInstructions(services, plan);
        instructions[AgentType.Testing] = FormatTestInstructions(services, plan);
        instructions[AgentType.Security] = $"Scan all generated code for: OWASP Top 10, PHI exposure, missing auth, hardcoded secrets. Services: {serviceList}";
        instructions[AgentType.Review] = $"Full review pass: requirement traceability, code coverage, compliance checks, naming conventions. Focus on: {serviceList}";

        return instructions;
    }

    private static string FormatDbInstructions(List<ServicePlan> services, ImplementationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DATABASE IMPLEMENTATION PLAN:");
        foreach (var svc in services)
        {
            sb.AppendLine($"  Service: {svc.ServiceName} (schema: {svc.Schema}, port: {svc.Port})");
            sb.AppendLine($"  Entities: {string.Join(", ", svc.Entities)}");
            sb.AppendLine($"  Requirements: TenantId on all entities, audit columns (CreatedAt/By, UpdatedAt/By)");
            sb.AppendLine($"  RLS: CREATE POLICY per entity for tenant isolation");
            if (svc.EntitySchemas.Count > 0)
                sb.AppendLine($"  Existing schema info: {svc.EntitySchemas.Count} entities already defined — extend, don't replace");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatServiceInstructions(List<ServicePlan> services, ImplementationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SERVICE LAYER IMPLEMENTATION PLAN:");
        foreach (var svc in services)
        {
            sb.AppendLine($"  Service: {svc.ServiceName}");
            sb.AppendLine($"  Generate per entity: DTOs (Create/Update/Response), I{{Entity}}Service interface, {{Entity}}Service implementation");
            sb.AppendLine($"  Kafka events: publish {{Entity}}Created/Updated events via outbox pattern");
            if (svc.ApiContracts.Count > 0)
                sb.AppendLine($"  Existing API contracts: {svc.ApiContracts.Count} endpoints — ensure backward compatibility");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatAppInstructions(List<ServicePlan> services, ImplementationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("APPLICATION LAYER IMPLEMENTATION PLAN:");
        sb.AppendLine("  API Gateway: YARP reverse proxy routing to all services");
        foreach (var svc in services)
        {
            sb.AppendLine($"  Service: {svc.ServiceName} (port: {svc.Port})");
            sb.AppendLine($"  Endpoints: MapGroup per entity with GET/POST/PUT");
            sb.AppendLine($"  Middleware: TenantMiddleware, CorrelationId, GlobalExceptionHandler");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatIntegrationInstructions(List<ServicePlan> services, ImplementationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("INTEGRATION LAYER IMPLEMENTATION PLAN:");
        foreach (var svc in services.Where(s => s.PublishedEvents.Count > 0 || s.ConsumedEvents.Count > 0))
        {
            sb.AppendLine($"  Service: {svc.ServiceName}");
            sb.AppendLine($"  Publishes: {string.Join(", ", svc.PublishedEvents)}");
            sb.AppendLine($"  Consumes: {string.Join(", ", svc.ConsumedEvents)} (from: {string.Join(", ", svc.DependsOn)})");
            sb.AppendLine($"  Pattern: Transactional outbox + consume with manual Kafka commit");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatTestInstructions(List<ServicePlan> services, ImplementationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TESTING IMPLEMENTATION PLAN:");
        foreach (var svc in services)
        {
            sb.AppendLine($"  Service: {svc.ServiceName}");
            sb.AppendLine($"  Required: Unit tests (service logic), Repository tests (EF InMemory), Tenant isolation tests");
            sb.AppendLine($"  Coverage: Minimum 80% line coverage. Must test: happy path, validation failure, auth failure, tenant isolation");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ─── Identify standards and patterns to follow ───

    private static List<string> IdentifyStandards(AgentContext context)
    {
        return
        [
            "HIPAA §164.312 — Access Control, Audit, PHI Encryption, Minimum Necessary",
            "SOC 2 Type II — CC6 (Access), CC7 (Monitoring), CC8 (Change Management)",
            "OWASP Top 10 2025 — Injection, Broken Access, Cryptographic Failures, SSRF",
            "HL7 FHIR R4 — Patient, Encounter, Observation, DiagnosticReport resources",
            "Clean Architecture — Domain → Application → Infrastructure → WebAPI",
            "Multi-Tenant — Schema-per-service, TenantId column, EF query filters, RLS",
            "Event-Driven — Kafka with transactional outbox, idempotent consumers",
            "REST API — Minimal APIs, consistent error responses, pagination, HATEOAS links",
            "Observability — OpenTelemetry, Prometheus, structured logging, correlation IDs",
            "Testing — xUnit + Moq, arrange-act-assert, test coverage > 80%"
        ];
    }

    private async Task EnhancePlanWithLlmAsync(ImplementationPlan plan, AgentContext context, CancellationToken ct)
    {
        var serviceSummary = string.Join(", ", plan.AffectedServices.Take(10).Select(s => s.ServiceName));
        var reqCount = context.Requirements.Count;
        var concerns = string.Join(", ", plan.CrossCuttingConcerns.Take(5));

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a senior architect reviewing an implementation plan for a healthcare HMS.
                Identify any missing concerns, risky execution orderings, or overlooked dependencies.
                Be concise. Output a numbered list of recommendations (max 10).
                """,
            UserPrompt = $"""
                Implementation plan review:
                - {reqCount} requirements, {plan.AffectedServices.Count} affected services: {serviceSummary}
                - Execution order: {string.Join(" → ", plan.ExecutionOrder.Take(8).Select(e => $"{e.ServiceName}:{e.Layer}"))}
                - Cross-cutting: {concerns}
                - {plan.AgentInstructions.Count} agent instructions defined

                What risks or gaps exist? What should be added or reordered?
                """,
            Temperature = 0.3,
            MaxTokens = 1500,
            RequestingAgent = Name
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                plan.LlmReviewNotes = response.Content;
                context.OrchestratorInstructions.Add($"[PLAN-LLM-REVIEW] {response.Content}");
                _logger.LogInformation("LLM reviewed implementation plan — added notes");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM plan review skipped");
        }
    }

    private static string FormatPlanSummary(ImplementationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"IMPLEMENTATION_PLAN iteration={plan.Iteration}");
        sb.AppendLine($"SERVICES={string.Join(",", plan.AffectedServices.Select(s => s.ServiceName))}");
        sb.AppendLine($"STEPS={plan.ExecutionOrder.Count}");
        sb.AppendLine($"CONCERNS={string.Join(",", plan.CrossCuttingConcerns.Select(c => c.Name))}");
        sb.AppendLine($"STANDARDS={plan.Standards.Count}");
        return sb.ToString().TrimEnd();
    }
}
