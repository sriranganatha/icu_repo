using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Architecture;

/// <summary>
/// Produces architecture guidance for downstream implementation agents.
/// Guidance is published as orchestrator instructions and an ADR-style artifact.
/// </summary>
public sealed class ArchitectAgent : IAgent
{
    private readonly ILogger<ArchitectAgent> _logger;

    public AgentType Type => AgentType.Architect;
    public string Name => "Architect Agent";
    public string Description => "Derives bounded-context architecture guidance and target services from requirements.";

    public ArchitectAgent(ILogger<ArchitectAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        try
        {
            var selectedServices = SelectServicesFromRequirements(context.Requirements).ToList();
            if (selectedServices.Count == 0)
                selectedServices = MicroserviceCatalog.All.ToList();

            var principles = BuildPrinciples(context);
            var instruction = BuildArchitectureInstruction(selectedServices, principles);
            UpsertInstruction(context, instruction, "[ARCH]");

            var serviceList = string.Join(", ", selectedServices.Select(s => s.Name));
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

                    ## Principles
                    - {string.Join("\n- ", principles)}

                    ## Instruction Contract
                    - Prefix: [ARCH]
                    - TARGET_SERVICES: comma-separated service names from MicroserviceCatalog
                    - PRINCIPLES: architecture rules that implementation agents must follow
                    - RULES: mandatory implementation behavior

                    ## Current Instruction
                    {instruction}
                    """
            };

            context.Artifacts.Add(artifact);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Architecture guidance ready for {selectedServices.Count} services: {serviceList}");

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Architecture guidance published for {selectedServices.Count} services with {principles.Count} principles",
                Artifacts = [artifact],
                Messages =
                [
                    new AgentMessage
                    {
                        From = Type,
                        To = AgentType.Orchestrator,
                        Subject = "Architecture guidance published",
                        Body = $"TARGET_SERVICES={serviceList}; PRINCIPLES={principleList}"
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

    private static List<MicroserviceDefinition> SelectServicesFromRequirements(IEnumerable<Requirement> requirements)
    {
        var text = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}")).ToLowerInvariant();
        var selected = new Dictionary<string, MicroserviceDefinition>(StringComparer.OrdinalIgnoreCase);

        void Add(string serviceName)
        {
            var svc = MicroserviceCatalog.ByName(serviceName);
            if (svc is not null)
                selected[svc.Name] = svc;
        }

        if (ContainsAny(text, "patient", "demographic", "mpi", "identifier")) Add("PatientService");
        if (ContainsAny(text, "encounter", "visit", "clinical note")) Add("EncounterService");
        if (ContainsAny(text, "admission", "inpatient", "bed")) Add("InpatientService");
        if (ContainsAny(text, "emergency", "triage", "ed")) Add("EmergencyService");
        if (ContainsAny(text, "diagnostic", "lab", "result")) Add("DiagnosticsService");
        if (ContainsAny(text, "revenue", "billing", "claim", "payer")) Add("RevenueService");
        if (ContainsAny(text, "audit", "compliance", "soc2", "hipaa")) Add("AuditService");
        if (ContainsAny(text, "ai", "model", "assistant")) Add("AiService");

        var withDependencies = new Dictionary<string, MicroserviceDefinition>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var svc in selected.Values.ToList())
        {
            foreach (var dep in svc.DependsOn)
            {
                var depSvc = MicroserviceCatalog.ByName(dep);
                if (depSvc is not null)
                    withDependencies[depSvc.Name] = depSvc;
            }
        }

        return withDependencies.Values.OrderBy(s => s.Name).ToList();
    }

    private static List<string> BuildPrinciples(AgentContext context)
    {
        var text = string.Join(" ", context.Requirements.Select(r => r.Description)).ToLowerInvariant();
        var principles = new List<string>
        {
            "Bounded-context ownership per service",
            "Tenant isolation in data and APIs",
            "Backwards-compatible API contracts",
            "Event-driven integration using outbox pattern"
        };

        if (ContainsAny(text, "hipaa", "soc2", "compliance"))
            principles.Add("Compliance-by-design with full auditability");

        if (ContainsAny(text, "scale", "throughput", "latency", "performance"))
            principles.Add("Horizontal scalability with asynchronous workflows");

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
