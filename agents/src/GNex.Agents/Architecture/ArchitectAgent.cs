using System.Diagnostics;
using System.Text.Json;
using GNex.Core.Enums;
using GNex.Core.Extensions;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Architecture;

/// <summary>
/// Produces architecture guidance for downstream implementation agents.
/// Uses LLM to derive bounded-context microservices from project requirements,
/// making the platform fully domain-agnostic. Guidance is published as orchestrator
/// instructions and an ADR-style artifact. Derived services are stored in
/// <see cref="AgentContext.DerivedServices"/> for all downstream agents.
/// </summary>
public sealed class ArchitectAgent : IAgent
{
    private readonly ILogger<ArchitectAgent> _logger;
    private readonly ILlmProvider _llm;

    public AgentType Type => AgentType.Architect;
    public string Name => "Architect Agent";
    public string Description => "Derives bounded-context architecture guidance and target services from requirements via LLM.";

    public ArchitectAgent(ILogger<ArchitectAgent> logger, ILlmProvider llm)
    {
        _logger = logger;
        _llm = llm;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        try
        {
            // ── Read feedback from previous iterations ──
            var feedback = context.ReadFeedback(Type);
            if (feedback.Count > 0)
            {
                _logger.LogInformation("ArchitectAgent received {Count} feedback items", feedback.Count);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Incorporating {feedback.Count} feedback items from previous iterations");
            }

            // ── Use DomainProfile for domain-aware architecture decisions ──
            var profile = context.DomainProfile;
            if (profile is not null)
            {
                _logger.LogInformation("ArchitectAgent using DomainProfile: {Domain}, {EventCount} domain events, {RuleCount} business rules",
                    profile.Domain, profile.DomainEvents?.Count ?? 0, profile.BusinessRules?.Count ?? 0);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"DomainProfile active: {profile.Domain} — {profile.DomainEvents?.Count ?? 0} events, {profile.BusinessRules?.Count ?? 0} rules, {profile.QualityAttributes?.Count ?? 0} quality attributes");
            }

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Analyzing requirements to derive bounded-context microservices via LLM...");

            // ── Derive services from requirements using LLM ──
            var derivedServices = await DeriveServicesFromRequirementsAsync(context, ct);

            if (derivedServices.Count > 0)
            {
                context.DerivedServices = derivedServices;
                _logger.LogInformation("ArchitectAgent derived {Count} microservices via LLM: {Services}",
                    derivedServices.Count, string.Join(", ", derivedServices.Select(s => s.Name)));
            }
            else
            {
                _logger.LogWarning("ArchitectAgent LLM derivation returned no services — pipeline will use fallback catalog");
                context.Findings.Add(new ReviewFinding
                {
                    Id = $"ARCH-FALLBACK-{context.RunId[..8]}",
                    Category = "Architecture",
                    Severity = ReviewSeverity.Critical,
                    Message = "LLM service derivation failed — falling back to generic catalog. " +
                              "Generated code may not match project requirements. " +
                              "Check LLM provider connectivity and retry.",
                    FilePath = "Architecture/ArchitectureGuidance.md"
                });
            }

            var activeServices = ServiceCatalogResolver.GetServices(context).ToList();
            var principles = await BuildPrinciplesAsync(context, ct);
            var instruction = BuildArchitectureInstruction(activeServices, principles);
            UpsertInstruction(context, instruction, "[ARCH]");

            var serviceList = string.Join(", ", activeServices.Select(s => s.Name));
            var principleList = string.Join(", ", principles);

            var artifact = new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "Architecture/ArchitectureGuidance.md",
                FileName = "ArchitectureGuidance.md",
                Namespace = "GNex.Architecture",
                ProducedBy = Type,
                Content = $"""
                    # Architecture Guidance

                    ## Scope
                    Target bounded contexts: {serviceList}
                    Source: {(context.DerivedServices.Count > 0 ? "LLM-derived from requirements" : "Fallback catalog")}

                    ## Derived Microservices
                    {string.Join("\n", activeServices.Select(s => $"- **{s.Name}** ({s.Schema}): {s.Description} — entities: [{string.Join(", ", s.Entities)}], port: {s.ApiPort}"))}

                    ## Principles
                    - {string.Join("\n- ", principles)}

                    ## Instruction Contract
                    - Prefix: [ARCH]
                    - TARGET_SERVICES: comma-separated service names
                    - PRINCIPLES: architecture rules that implementation agents must follow
                    - RULES: mandatory implementation behavior

                    ## Current Instruction
                    {instruction}
                    """
            };

            context.Artifacts.Add(artifact);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // ── Dispatch architecture findings as feedback for downstream agents ──
            context.DispatchFindingsAsFeedback(Type, context.Findings.ToList());

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type,
                    $"Architecture guidance ready — {activeServices.Count} services derived{(context.DerivedServices.Count > 0 ? " via LLM" : " (fallback)")}: {serviceList}");

            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Architecture guidance published for {activeServices.Count} services ({(context.DerivedServices.Count > 0 ? "LLM-derived" : "fallback")}) with {principles.Count} principles",
                Artifacts = [artifact],
                Messages =
                [
                    new AgentMessage
                    {
                        From = Type,
                        To = AgentType.Orchestrator,
                        Subject = "Architecture guidance published",
                        Body = $"TARGET_SERVICES={serviceList}; PRINCIPLES={principleList}; SOURCE={( context.DerivedServices.Count > 0 ? "LLM" : "fallback")}"
                    }
                ],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "ArchitectAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    /// <summary>
    /// Uses LLM to analyze requirements and produce a set of bounded-context microservice definitions.
    /// Returns empty list on failure so the pipeline can fall back to the legacy catalog.
    /// </summary>
    private async Task<List<MicroserviceDefinition>> DeriveServicesFromRequirementsAsync(
        AgentContext context, CancellationToken ct)
    {
        var requirementsSummary = string.Join("\n", context.Requirements
            .Take(150) // cap to avoid token overflow
            .Select((r, i) => $"{i + 1}. [{r.Module}] {r.Title}: {r.Description}"));

        if (string.IsNullOrWhiteSpace(requirementsSummary))
        {
            _logger.LogWarning("No requirements available for service derivation");
            return [];
        }

        // Build rich context from feedback, DomainProfile, quality metrics, and agent results
        var llmContext = context.BuildLlmContextBlock(Type);

        var prompt = new LlmPrompt
        {
            SystemPrompt = $$"""
                You are a senior software architect. Given a set of project requirements,
                decompose the system into bounded-context microservices following Domain-Driven Design.

                Each microservice must have:
                - Name: PascalCase ending in "Service" (e.g., "OrderService", "UserService")
                - ShortName: lowercase kebab-friendly identifier (e.g., "order", "user")
                - Schema: database schema name using format "ctx_name" (e.g., "ctx_order", "ctx_user")
                - Description: one-line description of the bounded context
                - ApiPort: starting from 5101, incrementing by 1
                - Entities: PascalCase entity names that belong to this context (2-6 per service)
                - DependsOn: array of other service Names this service depends on (empty if none)

                Rules:
                - Derive services ONLY from the given requirements — do NOT invent features not mentioned.
                - Each entity should belong to exactly one service.
                - Keep services focused (single responsibility).
                - Include cross-cutting services (Audit, Notification) only if requirements mention them.
                - Output ONLY valid JSON array — no markdown, no commentary.

                {{(!string.IsNullOrWhiteSpace(llmContext) ? llmContext : "")}}
                """,
            UserPrompt = $$"""
                Analyze these project requirements and derive bounded-context microservices:

                {{requirementsSummary}}

                Respond with a JSON array of microservice definitions:
                [
                  {
                    "Name": "ExampleService",
                    "ShortName": "example",
                    "Schema": "ctx_example",
                    "Description": "Description of what this service does",
                    "ApiPort": 5101,
                    "Entities": ["Entity1", "Entity2"],
                    "DependsOn": []
                  }
                ]
                """,
            Temperature = 0.3,
            MaxTokens = 4096,
            RequestingAgent = "ArchitectAgent"
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("LLM service derivation failed: {Error}", response.Error ?? "empty response");
                return [];
            }

            return ParseServiceDefinitions(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM service derivation threw — falling back to catalog");
            return [];
        }
    }

    /// <summary>
    /// Parse JSON array of service definitions from LLM response.
    /// Tolerant of markdown fences and minor formatting issues.
    /// </summary>
    private List<MicroserviceDefinition> ParseServiceDefinitions(string content)
    {
        // Strip markdown code fences if present
        var json = content.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline > 0)
                json = json[(firstNewline + 1)..];
            if (json.EndsWith("```"))
                json = json[..^3];
            json = json.Trim();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dtos = JsonSerializer.Deserialize<List<ServiceDto>>(json, options);
            if (dtos is null || dtos.Count == 0)
            {
                _logger.LogWarning("LLM returned empty or null service array");
                return [];
            }

            var services = new List<MicroserviceDefinition>();
            var usedPorts = new HashSet<int>();

            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) continue;

                // Ensure unique ports
                var port = dto.ApiPort;
                while (port < 5101 || usedPorts.Contains(port))
                    port = (usedPorts.Count > 0 ? usedPorts.Max() : 5100) + 1;
                usedPorts.Add(port);

                services.Add(new MicroserviceDefinition
                {
                    Name = dto.Name.Trim(),
                    ShortName = dto.ShortName?.Trim() ?? dto.Name.Replace("Service", "").ToLowerInvariant(),
                    Schema = dto.Schema?.Trim() ?? $"ctx_{dto.Name.Replace("Service", "").ToLowerInvariant()}",
                    Description = dto.Description?.Trim() ?? dto.Name,
                    ApiPort = port,
                    Entities = dto.Entities ?? [],
                    DependsOn = dto.DependsOn ?? []
                });
            }

            _logger.LogInformation("Parsed {Count} microservice definitions from LLM response", services.Count);
            return services;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM service derivation JSON");
            return [];
        }
    }

    private sealed class ServiceDto
    {
        public string? Name { get; set; }
        public string? ShortName { get; set; }
        public string? Schema { get; set; }
        public string? Description { get; set; }
        public int ApiPort { get; set; }
        public string[]? Entities { get; set; }
        public string[]? DependsOn { get; set; }
    }

    private async Task<List<string>> BuildPrinciplesAsync(AgentContext context, CancellationToken ct)
    {
        var llmContext = context.BuildLlmContextBlock(Type);
        var requirementsSummary = string.Join("\n", context.Requirements
            .Take(50)
            .Select(r => $"- [{r.Module}] {r.Title}: {r.Description}"));

        var prompt = new LlmPrompt
        {
            SystemPrompt = $$"""
                You are a senior software architect. Given a set of project requirements and domain context,
                derive 5-10 architecture principles that implementation agents must follow.

                {{(!string.IsNullOrWhiteSpace(llmContext) ? llmContext : "")}}

                Output ONLY a JSON array of principle strings. No markdown, no commentary.
                Example: ["Bounded-context ownership per service", "Tenant isolation in data and APIs"]
                """,
            UserPrompt = $"Requirements:\n{requirementsSummary}",
            Temperature = 0.3,
            MaxTokens = 1024,
            RequestingAgent = "ArchitectAgent"
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var json = response.Content.Trim();
                if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
                if (json.EndsWith("```")) json = json[..^3].Trim();

                var principles = JsonSerializer.Deserialize<List<string>>(json);
                if (principles is { Count: > 0 })
                    return principles;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM principle derivation failed, using fallback");
        }

        // Fallback to static principles
        return BuildPrinciplesFallback(context);
    }

    private static List<string> BuildPrinciplesFallback(AgentContext context)
    {
        var text = string.Join(" ", context.Requirements.Select(r => r.Description)).ToLowerInvariant();
        var principles = new List<string>
        {
            "Bounded-context ownership per service",
            "Tenant isolation in data and APIs",
            "Backwards-compatible API contracts",
            "Event-driven integration using outbox pattern"
        };

        if (ContainsAny(text, "compliance", "audit", "regulation", "gdpr", "hipaa", "soc2"))
            principles.Add("Compliance-by-design with full auditability");

        if (ContainsAny(text, "scale", "throughput", "latency", "performance"))
            principles.Add("Horizontal scalability with asynchronous workflows");

        if (ContainsAny(text, "security", "encrypt", "authentication", "authorization"))
            principles.Add("Security-first with defense-in-depth");

        // Enrich from DomainProfile if available
        var profile = context.DomainProfile;
        if (profile is not null)
        {
            // Add compliance-derived principles
            foreach (var fw in profile.ComplianceFrameworks ?? [])
                principles.Add($"{fw.Name} compliance ({string.Join(", ", fw.KeyClauses.Take(3))})");

            // Add quality attributes as architectural principles
            foreach (var qa in profile.QualityAttributes?.Take(5) ?? [])
                principles.Add(qa);
        }

        return principles;
    }

    private static string BuildArchitectureInstruction(IEnumerable<MicroserviceDefinition> services, IEnumerable<string> principles)
    {
        var serviceCsv = string.Join(",", services.Select(s => s.Name));
        var principleCsv = string.Join("|", principles.Select(p => p.Replace(";", string.Empty)));
        return $"[ARCH] TARGET_SERVICES={serviceCsv}; PRINCIPLES={principleCsv}; RULES=Generate only scoped bounded contexts and respect dependency order";
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static void UpsertInstruction(AgentContext context, string instruction, string prefix)
    {
        context.OrchestratorInstructions.RemoveAll(i => i.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        context.OrchestratorInstructions.Add(instruction);
    }
}
