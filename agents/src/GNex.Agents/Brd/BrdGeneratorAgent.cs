using System.Diagnostics;
using System.Text;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Brd;

/// <summary>
/// Generates Business Requirement Documents from approved requirements.
/// Produces structured BRD with sections, diagrams, and traceability links.
/// </summary>
public sealed class BrdGeneratorAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<BrdGeneratorAgent> _logger;

    public AgentType Type => AgentType.BrdGenerator;
    public string Name => "BRD Generator";
    public string Description => "Generates Business Requirement Documents with structured sections and Mermaid diagrams.";

    public BrdGeneratorAgent(ILlmProvider llm, ILogger<BrdGeneratorAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        var requirements = context.Requirements;
        if (requirements.Count == 0)
        {
            context.AgentStatuses[Type] = AgentStatus.Completed;
            return new AgentResult { Agent = Type, Success = true, Summary = "No requirements to generate BRD from.", Duration = sw.Elapsed };
        }

        if (context.ReportProgress is not null)
            await context.ReportProgress(Type, $"Generating BRD from {requirements.Count} requirements");

        var brd = BuildBrd(requirements, context);
        
        // Enrich key sections with LLM
        await EnrichBrdWithLlmAsync(brd, requirements, ct);
        
        context.BrdDocuments.Add(brd);

        // Generate Mermaid diagrams
        brd.ContextDiagram = BrdDiagramGenerator.GenerateContextDiagram(requirements, context.DomainModel, context.DomainProfile);
        brd.DataFlowDiagram = BrdDiagramGenerator.GenerateDataFlowDiagram(requirements, context.DomainModel);
        brd.SequenceDiagram = BrdDiagramGenerator.GenerateSequenceDiagram(requirements);
        brd.ErDiagram = BrdDiagramGenerator.GenerateErDiagram(context.DomainModel, context.DomainProfile);

        // Produce BRD as markdown artifact
        var markdown = BrdMarkdownExporter.Export(brd);
        context.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Documentation,
            RelativePath = "Docs/BRD/business-requirements-document.md",
            FileName = "business-requirements-document.md",
            Namespace = "GNex.Docs",
            ProducedBy = Type,
            Content = markdown,
            TracedRequirementIds = brd.TracedRequirementIds
        });

        // Produce diagrams as separate artifacts
        if (!string.IsNullOrWhiteSpace(brd.ContextDiagram))
        {
            context.Artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "Docs/BRD/diagrams/context-diagram.md",
                FileName = "context-diagram.md",
                Namespace = "GNex.Docs.Diagrams",
                ProducedBy = Type,
                Content = $"# System Context Diagram\n\n```mermaid\n{brd.ContextDiagram}\n```\n",
                TracedRequirementIds = brd.TracedRequirementIds
            });
        }

        if (!string.IsNullOrWhiteSpace(brd.ErDiagram))
        {
            context.Artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "Docs/BRD/diagrams/er-diagram.md",
                FileName = "er-diagram.md",
                Namespace = "GNex.Docs.Diagrams",
                ProducedBy = Type,
                Content = $"# Entity Relationship Diagram\n\n```mermaid\n{brd.ErDiagram}\n```\n",
                TracedRequirementIds = brd.TracedRequirementIds
            });
        }

        if (!string.IsNullOrWhiteSpace(brd.SequenceDiagram))
        {
            context.Artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "Docs/BRD/diagrams/sequence-diagram.md",
                FileName = "sequence-diagram.md",
                Namespace = "GNex.Docs.Diagrams",
                ProducedBy = Type,
                Content = $"# Sequence Diagram\n\n```mermaid\n{brd.SequenceDiagram}\n```\n",
                TracedRequirementIds = brd.TracedRequirementIds
            });
        }

        // Complete claimed items
        foreach (var item in context.CurrentClaimedItems)
            context.CompleteWorkItem?.Invoke(item);

        _logger.LogInformation("BRD generated: {Title} with {Sections} sections, {Diagrams} diagrams",
            brd.Title, CountSections(brd), CountDiagrams(brd));

        context.AgentStatuses[Type] = AgentStatus.Completed;
        return new AgentResult
        {
            Agent = Type,
            Success = true,
            Summary = $"Generated BRD '{brd.Title}' with {CountSections(brd)} sections and {CountDiagrams(brd)} diagrams.",
            Duration = sw.Elapsed
        };
    }

    private async Task EnrichBrdWithLlmAsync(BrdDocument brd, List<Requirement> requirements, CancellationToken ct)
    {
        var reqSummary = string.Join("\n", requirements.Select(r => $"- [{r.Id}] {r.Title}: {Truncate(r.Description, 200)}").Take(50));

        var prompt = new LlmPrompt
        {
            SystemPrompt = """
                You are a senior business analyst writing BRD sections for an enterprise software system.
                Respond ONLY with the requested section content — no preamble, no markdown headers, no explanations.
                Be specific, detailed, and tie every statement back to the requirements provided.
                """,
            UserPrompt = $"""
                Given these {requirements.Count} requirements:
                {reqSummary}

                Generate an Executive Summary (2-3 paragraphs) that:
                1. States the business problem being solved
                2. Lists key capabilities being delivered
                3. Summarizes expected outcomes and success metrics
                4. References specific requirement IDs

                Then on a new line starting with "===PROJECT_SCOPE===" generate a Project Scope (1-2 paragraphs):
                1. What is in scope with specific deliverables
                2. Key technical boundaries
                3. Integration touchpoints

                Then on a new line starting with "===RISKS===" generate 5 project-specific risks in format:
                RISK|description|impact|likelihood|mitigation
                """,
            Temperature = 0.3,
            MaxTokens = 3000,
            RequestingAgent = Name
        };

        try
        {
            var response = await _llm.GenerateAsync(prompt, ct);
            if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            {
                var content = response.Content;
                var scopeIdx = content.IndexOf("===PROJECT_SCOPE===", StringComparison.OrdinalIgnoreCase);
                var riskIdx = content.IndexOf("===RISKS===", StringComparison.OrdinalIgnoreCase);

                if (scopeIdx > 0)
                    brd.ExecutiveSummary = content[..scopeIdx].Trim();

                if (scopeIdx > 0 && riskIdx > scopeIdx)
                    brd.ProjectScope = content[(scopeIdx + "===PROJECT_SCOPE===".Length)..riskIdx].Trim();
                else if (scopeIdx > 0)
                    brd.ProjectScope = content[(scopeIdx + "===PROJECT_SCOPE===".Length)..].Trim();

                if (riskIdx > 0)
                {
                    var riskSection = content[(riskIdx + "===RISKS===".Length)..].Trim();
                    var riskLines = riskSection.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var parsedRisks = new List<BrdRisk>();
                    foreach (var line in riskLines)
                    {
                        var parts = line.TrimStart('-', ' ').Split('|');
                        if (parts.Length >= 4)
                        {
                            parsedRisks.Add(new BrdRisk
                            {
                                Description = parts[0].Replace("RISK", "").Trim(),
                                Impact = parts[1].Trim(),
                                Likelihood = parts[2].Trim(),
                                Mitigation = parts[3].Trim()
                            });
                        }
                    }
                    if (parsedRisks.Count > 0) brd.Risks = parsedRisks;
                }

                _logger.LogInformation("BRD sections enriched via LLM ({Model})", response.Model);
            }
            else
            {
                _logger.LogWarning("LLM enrichment failed for BRD, using template defaults: {Error}", response.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM enrichment skipped for BRD — using template content");
        }
    }

    private static BrdDocument BuildBrd(List<Requirement> requirements, AgentContext context)
    {
        var modules = requirements.Select(r => r.Module).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
        var projectName = modules.Count > 0 ? string.Join(", ", modules.Take(3)) : context.PipelineConfig?.ProjectLabel ?? "Application";

        var brd = new BrdDocument
        {
            RunId = context.RunId,
            Title = $"{projectName} — Business Requirements Document",
            TracedRequirementIds = requirements.Select(r => r.Id).ToList()
        };

        // Executive Summary
        brd.ExecutiveSummary = $"This document specifies the business requirements for the {projectName} module(s) " +
                               $"of the software system. It covers {requirements.Count} requirements across " +
                               $"{modules.Count} module(s), addressing functional capabilities, non-functional constraints, " +
                               "integration points, and compliance mandates.";

        // Scope
        brd.ProjectScope = $"Deliver production-ready implementation of {requirements.Count} requirements for: {string.Join(", ", modules)}.";
        brd.InScope = string.Join("\n", requirements.Select(r => $"- [{r.Id}] {r.Title}"));
        brd.OutOfScope = "- Manual data migration from legacy systems\n- Third-party vendor integrations not specified in requirements\n- Mobile native applications (web-responsive only)";

        // Stakeholders
        brd.Stakeholders = ExtractStakeholders(requirements, context.DomainProfile);

        // FR / NFR
        brd.FunctionalRequirements = requirements
            .Where(r => !r.Tags.Any(t => t.Contains("nfr", StringComparison.OrdinalIgnoreCase)))
            .Select(r => $"[{r.Id}] {r.Title}: {Truncate(r.Description, 200)}")
            .ToList();
        brd.NonFunctionalRequirements = requirements
            .Where(r => r.Tags.Any(t => t.Contains("nfr", StringComparison.OrdinalIgnoreCase)))
            .Select(r => $"[{r.Id}] {r.Title}: {Truncate(r.Description, 200)}")
            .ToList();
        if (brd.NonFunctionalRequirements.Count == 0)
        {
            brd.NonFunctionalRequirements =
            [
                "NFR-PERF: API response time < 200ms at P95 under normal load",
                "NFR-SEC: All sensitive data encrypted at rest (AES-256) and in transit (TLS 1.3)",
                "NFR-AVAIL: 99.9% uptime SLA with automated failover",
                "NFR-AUDIT: All data mutations produce immutable audit records"
            ];
        }

        // Assumptions and Constraints
        brd.Assumptions =
        [
            "PostgreSQL 16+ is the primary data store.",
            "The system runs on .NET 10 with microservice architecture.",
            "Kafka is used for async inter-service communication.",
            "Standard integration protocols apply."
        ];
        brd.Constraints =
        [
            "Must comply with applicable data protection regulations.",
            "Must support multi-tenant data isolation via TenantId.",
            "All APIs must be versioned and backward-compatible.",
            "Database migrations must be non-destructive (no data loss)."
        ];

        // Integration Points
        brd.IntegrationPoints = ExtractIntegrationPoints(requirements, context.DomainProfile);

        // Security
        brd.SecurityRequirements =
        [
            "Role-based access control (RBAC) for all endpoints.",
            "JWT-based authentication with short-lived tokens.",
            "Rate limiting on public-facing APIs.",
            "OWASP Top 10 compliance verified by SecurityAgent."
        ];

        // Performance
        brd.PerformanceRequirements =
        [
            "P95 API latency < 200ms under 100 concurrent users.",
            "Database queries optimized with proper indexing.",
            "Bulk operations use batching and pagination."
        ];

        // Data
        brd.DataRequirements = requirements
            .SelectMany(r => r.Tags.Where(t => t.Contains("data", StringComparison.OrdinalIgnoreCase) || t.Contains("entity", StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .Select(t => $"Data entity: {t}")
            .ToList();
        if (brd.DataRequirements.Count == 0)
            brd.DataRequirements = ["Refer to domain model and ER diagram for full data requirements."];

        // Risks
        brd.Risks =
        [
            new BrdRisk { Description = "Complex inter-service dependencies may cause cascading failures.", Impact = "High", Likelihood = "Medium", Mitigation = "Circuit breakers and retry policies per service." },
            new BrdRisk { Description = "Sensitive data exposure if access control is misconfigured.", Impact = "Critical", Likelihood = "Low", Mitigation = "Automated compliance scanning in pipeline." },
            new BrdRisk { Description = "Schema migration failures during deployment.", Impact = "High", Likelihood = "Medium", Mitigation = "Non-destructive migrations with rollback scripts." }
        ];

        // Dependencies
        brd.Dependencies = requirements
            .SelectMany(r => r.DependsOn)
            .Distinct()
            .Select(d => $"Depends on: {d}")
            .ToList();

        return brd;
    }

    private static List<string> ExtractStakeholders(List<Requirement> requirements, DomainProfile? domainProfile)
    {
        // Prefer DomainProfile actors if available (LLM-derived)
        if (domainProfile?.Actors is { Count: > 0 } actors)
            return actors.Select(a => $"{a.Name} — {a.Role}").ToList();

        // Generic keyword-based fallback
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Administrator", "System Administrator"
        };
        var text = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}")).ToLowerInvariant();
        if (text.Contains("user") || text.Contains("customer") || text.Contains("client")) result.Add("End User");
        if (text.Contains("operator") || text.Contains("staff")) result.Add("Operator");
        if (text.Contains("manager") || text.Contains("supervisor")) result.Add("Manager");
        if (text.Contains("billing") || text.Contains("finance")) result.Add("Finance Staff");
        if (text.Contains("analyst") || text.Contains("report")) result.Add("Analyst");
        if (text.Contains("auditor") || text.Contains("compliance")) result.Add("Auditor");
        if (text.Contains("api") || text.Contains("integration")) result.Add("External System (API Consumer)");
        if (text.Contains("support") || text.Contains("helpdesk")) result.Add("Support Staff");
        return [.. result];
    }

    private static List<string> ExtractIntegrationPoints(List<Requirement> requirements, DomainProfile? domainProfile)
    {
        // Prefer DomainProfile integration patterns if available (LLM-derived)
        if (domainProfile?.IntegrationPatterns is { Count: > 0 } patterns)
            return patterns.Select(p => $"{p.Name} — {p.AdapterDescription ?? p.Applicability}").ToList();

        // Generic keyword-based fallback
        var points = new List<string>();
        var text = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}")).ToLowerInvariant();
        if (text.Contains("kafka") || text.Contains("event")) points.Add("Apache Kafka — Async event streaming between microservices");
        if (text.Contains("email") || text.Contains("notification")) points.Add("SMTP / Push Notifications — Alert delivery");
        if (text.Contains("ldap") || text.Contains("active directory")) points.Add("LDAP / Active Directory — User authentication");
        if (text.Contains("api") || text.Contains("integration")) points.Add("REST/gRPC APIs — Service-to-service communication");
        if (points.Count == 0) points.Add("Internal microservice APIs via HTTP/gRPC");
        return points;
    }

    private static int CountSections(BrdDocument brd)
    {
        var count = 0;
        if (!string.IsNullOrEmpty(brd.ExecutiveSummary)) count++;
        if (!string.IsNullOrEmpty(brd.ProjectScope)) count++;
        if (brd.Stakeholders.Count > 0) count++;
        if (brd.FunctionalRequirements.Count > 0) count++;
        if (brd.NonFunctionalRequirements.Count > 0) count++;
        if (brd.Assumptions.Count > 0) count++;
        if (brd.Constraints.Count > 0) count++;
        if (brd.IntegrationPoints.Count > 0) count++;
        if (brd.SecurityRequirements.Count > 0) count++;
        if (brd.PerformanceRequirements.Count > 0) count++;
        if (brd.Risks.Count > 0) count++;
        return count;
    }

    private static int CountDiagrams(BrdDocument brd)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(brd.ContextDiagram)) count++;
        if (!string.IsNullOrWhiteSpace(brd.DataFlowDiagram)) count++;
        if (!string.IsNullOrWhiteSpace(brd.SequenceDiagram)) count++;
        if (!string.IsNullOrWhiteSpace(brd.ErDiagram)) count++;
        return count;
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
