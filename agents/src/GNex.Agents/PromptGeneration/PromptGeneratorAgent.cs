using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GNex.Core.Enums;
using GNex.Core.Extensions;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.PromptGeneration;

/// <summary>
/// LLM-powered agent that generates the <see cref="DomainProfile"/> for a project.
/// Runs early in the pipeline (after RequirementsReader) and derives:
///   • Actors and personas from the domain + requirements
///   • Applicable compliance frameworks (HIPAA for healthcare, PCI-DSS for fintech, etc.)
///   • Domain-specific integration patterns (FHIR/HL7 for healthcare, FIX for trading, etc.)
///   • Sensitive data field patterns
///   • Agent-specific system prompts tailored to the domain
///   • Fallback ER diagram when no domain model exists yet
///
/// All downstream agents read from <c>context.DomainProfile</c> instead of
/// relying on hardcoded domain knowledge.
/// </summary>
public sealed class PromptGeneratorAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<PromptGeneratorAgent> _logger;

    public AgentType Type => AgentType.PromptGenerator;
    public string Name => "Prompt Generator Agent";
    public string Description => "Generates domain-specific actors, compliance frameworks, integration patterns, and LLM prompts for all downstream agents.";

    public PromptGeneratorAgent(ILlmProvider llm, ILogger<PromptGeneratorAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;

        var config = context.PipelineConfig;
        var domain = config?.ProjectDomain ?? "";
        var domainDesc = config?.DomainContext ?? "a generic software platform";

        _logger.LogInformation("PromptGeneratorAgent starting — domain: '{Domain}', description: '{Desc}'",
            domain, domainDesc);

        try
        {
            var profile = new DomainProfile
            {
                Domain = domain,
                DomainDescription = domainDesc
            };

            // ── Step 1: Derive actors/personas via LLM ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Deriving actors and personas for '{domain}' domain from {context.Requirements.Count} requirements...");

            profile.Actors = await DeriveActorsAsync(domain, domainDesc, context.Requirements, ct);
            _logger.LogInformation("Derived {Count} actors for domain '{Domain}'", profile.Actors.Count, domain);

            // ── Step 2: Derive compliance frameworks ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Identifying applicable compliance frameworks for '{domain}' domain...");

            profile.ComplianceFrameworks = await DeriveComplianceFrameworksAsync(domain, domainDesc, context.Requirements, ct);
            _logger.LogInformation("Identified {Count} compliance frameworks", profile.ComplianceFrameworks.Count);

            // ── Step 3: Derive integration patterns ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Identifying integration patterns for '{domain}' domain...");

            profile.IntegrationPatterns = await DeriveIntegrationPatternsAsync(domain, domainDesc, context.Requirements, ct);

            // ── Step 4: Derive sensitive data patterns ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Identifying sensitive/regulated data field patterns...");

            profile.SensitiveFieldPatterns = await DeriveSensitiveFieldsAsync(domain, domainDesc, ct);

            // ── Step 5: Derive domain requirement tags ──
            profile.DomainRequirementTags = await DeriveDomainTagsAsync(domain, domainDesc, context.Requirements, ct);

            // ── Step 5b: Derive domain glossary ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Building domain glossary — canonical terminology for consistent code/doc naming...");

            profile.DomainGlossary = await DeriveDomainGlossaryAsync(domain, domainDesc, context.Requirements, ct);

            // ── Step 5c: Derive business rules / invariants ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Extracting domain business rules and invariants...");

            profile.BusinessRules = await DeriveBusinessRulesAsync(domain, domainDesc, context.Requirements, ct);

            // ── Step 5d: Derive quality attributes ──
            profile.QualityAttributes = await DeriveQualityAttributesAsync(domain, domainDesc, context.Requirements, ct);

            // ── Step 5e: Derive domain events ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Deriving canonical domain events for event-driven architecture...");

            profile.DomainEvents = await DeriveDomainEventsAsync(domain, domainDesc, profile.Actors, context.Requirements, ct);

            // ── Step 6: Generate fallback ER diagram ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating domain-appropriate fallback ER diagram...");

            profile.FallbackErDiagram = await GenerateFallbackErDiagramAsync(domain, domainDesc, profile.Actors, ct);

            // ── Step 7: Generate agent-specific prompts ──
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating tailored system prompts for downstream agents...");

            profile.AgentPrompts = await GenerateAgentPromptsAsync(domain, domainDesc, profile, context, ct);

            // Set the profile on the context for all downstream agents
            context.DomainProfile = profile;
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Emit profile as a documentation artifact
            context.Artifacts.Add(new CodeArtifact
            {
                Layer = ArtifactLayer.Documentation,
                RelativePath = "docs/domain-profile.json",
                FileName = "domain-profile.json",
                Namespace = "GNex.Documentation",
                ProducedBy = Type,
                Content = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true })
            });

            return new AgentResult
            {
                Agent = Type,
                Success = true,
                Summary = $"Domain profile generated: {profile.Actors.Count} actors, {profile.ComplianceFrameworks.Count} compliance frameworks, {profile.IntegrationPatterns.Count} integration patterns, {profile.DomainEvents.Count} domain events, {profile.BusinessRules.Count} business rules, {profile.DomainGlossary.Count} glossary terms, {profile.AgentPrompts.Count} agent prompts",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PromptGeneratorAgent failed — falling back to minimal profile");
            context.DomainProfile = BuildFallbackProfile(domain, domainDesc);
            context.AgentStatuses[Type] = AgentStatus.Completed; // Don't block pipeline
            return new AgentResult
            {
                Agent = Type,
                Success = true, // Soft failure — fallback profile is usable
                Summary = $"PromptGenerator used fallback profile (LLM error: {ex.Message})",
                Duration = sw.Elapsed
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ACTOR DERIVATION
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<DomainActor>> DeriveActorsAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var reqSummary = string.Join("\n", requirements.Take(30).Select(r => $"- {r.Id}: {r.Title} — {r.Description}"));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a domain analysis expert. Given a project domain and its requirements,
                identify ALL actors and personas who will interact with the system.
                
                For each actor, provide:
                - Name: The role name (e.g. "Physician", "Trader", "Shopper")
                - Role: Brief role description
                - Description: What they do in the system
                - TypicalUseCases: 2-4 typical use cases
                
                Output ONLY a JSON array of actors. No markdown, no explanation.
                Example:
                [
                  {"Name":"Administrator","Role":"System admin","Description":"Manages users and configuration","TypicalUseCases":["User management","System configuration","Audit review"]},
                  {"Name":"End User","Role":"Primary user","Description":"Uses core features","TypicalUseCases":["Browse catalog","Place order","Track delivery"]}
                ]
                
                Always include "Administrator" and "System" (for automated processes).
                Derive domain-specific actors from the requirements — do NOT use generic placeholders.
                """,
            UserPrompt = $"""
                Domain: {domain}
                Domain Description: {domainDesc}
                
                Requirements:
                {reqSummary}
                
                Identify all actors/personas for this system. Return JSON array only.
                """,
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var actors = JsonSerializer.Deserialize<List<DomainActor>>(
                    ExtractJson(response.Content),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (actors is { Count: > 0 })
                    return actors;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse actor JSON — using fallback");
            }
        }

        return BuildFallbackActors(domain);
    }

    // ═══════════════════════════════════════════════════════════════════
    // COMPLIANCE FRAMEWORK DERIVATION
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<ComplianceFramework>> DeriveComplianceFrameworksAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var reqText = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}"));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a compliance and regulatory expert. Given a project domain and requirements,
                identify ALL applicable compliance frameworks.
                
                Rules:
                - SOC2 is ALWAYS applicable (universal for all SaaS/software)
                - HIPAA only for Healthcare/Medical/Clinical/Pharma domains
                - PCI-DSS only for payment processing / FinTech / E-Commerce with payment
                - GDPR for any system handling EU user data
                - FERPA only for Education domains
                - CCPA for systems with California users
                
                For each framework:
                - Name: Framework name (e.g. "HIPAA", "SOC2")
                - IsUniversal: true if it applies to ALL projects (only SOC2)
                - KeyClauses: 2-4 key sections/clauses relevant to this project
                - ScanPrompt: A system prompt for an agent that scans code for compliance with this framework
                
                Output ONLY a JSON array. No markdown.
                """,
            UserPrompt = $"""
                Domain: {domain}
                Domain Description: {domainDesc}
                Requirements summary: {reqText[..Math.Min(reqText.Length, 2000)]}
                
                Return applicable compliance frameworks as JSON array.
                """,
            Temperature = 0.2,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var frameworks = JsonSerializer.Deserialize<List<ComplianceFramework>>(
                    ExtractJson(response.Content),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (frameworks is { Count: > 0 })
                    return frameworks;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse compliance JSON — using fallback");
            }
        }

        // Fallback: SOC2 always, HIPAA if healthcare
        var result = new List<ComplianceFramework>
        {
            new() { Name = "SOC2", IsUniversal = true, KeyClauses = ["CC6 - Logical Access", "CC7 - System Operations", "CC8 - Change Management"] }
        };
        if (domain.Contains("health", StringComparison.OrdinalIgnoreCase) ||
            domain.Contains("medical", StringComparison.OrdinalIgnoreCase))
            result.Add(new() { Name = "HIPAA", IsUniversal = false, KeyClauses = ["164.312(a) - Access Control", "164.312(b) - Audit Controls", "164.400 - Breach Notification"] });

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════
    // INTEGRATION PATTERN DERIVATION
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<IntegrationPattern>> DeriveIntegrationPatternsAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var reqText = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}"));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are an integration architecture expert. Given a domain and requirements,
                identify domain-specific integration protocols and standards.
                
                Examples:
                - Healthcare: FHIR R4, HL7 v2, DICOM
                - FinTech: FIX 4.4, ISO 20022, SWIFT
                - E-Commerce: EDI X12, Payment Gateway APIs
                - Manufacturing: OPC-UA, MQTT
                - Education: LTI, xAPI
                
                Also include universal patterns that apply to all domains:
                - Kafka / Message Bus (if async events are needed)
                - REST/gRPC APIs (always)
                - SMTP/Push Notifications (if alerts are needed)
                
                For each pattern, provide Name, Applicability (when it applies), AdapterDescription.
                Output ONLY a JSON array. No markdown.
                """,
            UserPrompt = $"""
                Domain: {domain}
                Domain Description: {domainDesc}
                Requirements: {reqText[..Math.Min(reqText.Length, 2000)]}
                
                Return integration patterns as JSON array.
                """,
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var patterns = JsonSerializer.Deserialize<List<IntegrationPattern>>(
                    ExtractJson(response.Content),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (patterns is { Count: > 0 })
                    return patterns;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse integration pattern JSON — using fallback");
            }
        }

        return [new IntegrationPattern { Name = "REST/gRPC APIs", Applicability = "Always", AdapterDescription = "Standard HTTP APIs for service communication" }];
    }

    // ═══════════════════════════════════════════════════════════════════
    // SENSITIVE FIELD DERIVATION
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<string>> DeriveSensitiveFieldsAsync(string domain, string domainDesc, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a data privacy expert. Given a project domain, list ALL field name patterns
                that contain sensitive/regulated data.
                
                Examples:
                - Healthcare (PHI): DateOfBirth, SSN, MedicalRecordNumber, Diagnosis, Prescription, etc.
                - FinTech (PII/PCI): CreditCardNumber, AccountNumber, TaxId, NetWorth, etc.
                - E-Commerce (PII): PaymentMethod, BillingAddress, CreditCard, etc.
                - Generic (PII): Email, Phone, Address, Password, Secret, Token
                
                Output ONLY a JSON array of strings (field name patterns). No markdown.
                Always include generic PII fields (Email, Phone, Address, Password, Secret, Token).
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\n\nReturn sensitive field patterns as JSON string array.",
            Temperature = 0.2,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var fields = JsonSerializer.Deserialize<List<string>>(
                    ExtractJson(response.Content));
                if (fields is { Count: > 0 })
                    return fields;
            }
            catch { /* fallback below */ }
        }

        return ["Email", "Phone", "Address", "Password", "Secret", "Token", "DateOfBirth", "SSN"];
    }

    // ═══════════════════════════════════════════════════════════════════
    // DOMAIN REQUIREMENT TAGS
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<string>> DeriveDomainTagsAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var existingTags = requirements
            .SelectMany(r => r.Tags)
            .Distinct()
            .ToList();

        if (existingTags.Count > 0)
            return existingTags;

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a requirements engineering expert. Given a project domain,
                list the primary module/feature tags that requirements would be categorized under.
                
                Examples:
                - Healthcare: ["Patient", "Encounter", "Diagnostics", "Billing", "Emergency", "Inpatient"]
                - E-Commerce: ["Product", "Order", "Payment", "Shipping", "Customer", "Inventory"]
                - FinTech: ["Account", "Transaction", "Portfolio", "Compliance", "Reporting"]
                
                Output ONLY a JSON array of tag strings. 6-12 tags.
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\n\nReturn domain tags as JSON string array.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var tags = JsonSerializer.Deserialize<List<string>>(ExtractJson(response.Content));
                if (tags is { Count: > 0 })
                    return tags;
            }
            catch { /* fallback below */ }
        }

        return ["Core", "Admin", "Integration", "Security", "Reporting"];
    }

    // ═══════════════════════════════════════════════════════════════════
    // DOMAIN GLOSSARY
    // ═══════════════════════════════════════════════════════════════════

    private async Task<Dictionary<string, string>> DeriveDomainGlossaryAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var reqSummary = string.Join("\n", requirements.Take(20).Select(r => $"- {r.Title}: {r.Description}"));
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a domain terminology expert. Given a project domain and requirements,
                build a glossary of 10-20 canonical terms with their definitions.
                
                The glossary ensures all agents use consistent naming in generated code,
                API endpoints, database columns, and documentation.
                
                Output ONLY a JSON object: { "Term": "Definition", ... }
                Example: { "Encounter": "A face-to-face interaction between a patient and provider", "Claim": "A request for payment submitted to a payer" }
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\n\nRequirements:\n{reqSummary}\n\nReturn glossary as JSON object.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var glossary = JsonSerializer.Deserialize<Dictionary<string, string>>(ExtractJson(response.Content));
                if (glossary is { Count: > 0 })
                    return new Dictionary<string, string>(glossary, StringComparer.OrdinalIgnoreCase);
            }
            catch { /* fallback below */ }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BUSINESS RULES
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<string>> DeriveBusinessRulesAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var reqSummary = string.Join("\n", requirements.Take(25).Select(r => $"- {r.Title}: {r.Description}"));
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a business analysis expert. Given a project domain and requirements,
                extract 5-15 key business rules and invariants that code must enforce.
                
                Business rules are domain constraints like:
                - "An order cannot be shipped until payment is confirmed"
                - "A patient encounter requires at least one assigned provider"
                - "A trade must settle within T+2 business days"
                
                Output ONLY a JSON array of rule strings. No markdown.
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\n\nRequirements:\n{reqSummary}\n\nReturn business rules as JSON string array.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var rules = JsonSerializer.Deserialize<List<string>>(ExtractJson(response.Content));
                if (rules is { Count: > 0 })
                    return rules;
            }
            catch { /* fallback below */ }
        }

        return [];
    }

    // ═══════════════════════════════════════════════════════════════════
    // QUALITY ATTRIBUTES
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<string>> DeriveQualityAttributesAsync(
        string domain, string domainDesc, List<Requirement> requirements, CancellationToken ct)
    {
        var nfrs = requirements.Where(r => r.Tags.Any(t => t.Contains("nfr", StringComparison.OrdinalIgnoreCase))).ToList();
        var reqSummary = nfrs.Count > 0
            ? string.Join("\n", nfrs.Take(15).Select(r => $"- {r.Title}: {r.Description}"))
            : string.Join("\n", requirements.Take(15).Select(r => $"- {r.Title}"));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a software quality expert. Given a domain and requirements,
                derive 5-10 measurable quality attributes / non-functional requirements.
                
                Format each as: "Category: Metric/Target"
                Examples:
                - "Performance: P95 API latency < 200ms under 500 concurrent users"
                - "Availability: 99.99% uptime for trading engine"
                - "Security: All PII encrypted at rest with AES-256"
                
                Output ONLY a JSON array of strings. No markdown.
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\n\nRequirements:\n{reqSummary}\n\nReturn quality attributes as JSON string array.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var attrs = JsonSerializer.Deserialize<List<string>>(ExtractJson(response.Content));
                if (attrs is { Count: > 0 })
                    return attrs;
            }
            catch { /* fallback below */ }
        }

        return ["Performance: P95 API latency < 200ms", "Availability: 99.9% uptime SLA", "Security: All sensitive data encrypted at rest"];
    }

    // ═══════════════════════════════════════════════════════════════════
    // DOMAIN EVENTS
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<DomainEvent>> DeriveDomainEventsAsync(
        string domain, string domainDesc, List<DomainActor> actors, List<Requirement> requirements, CancellationToken ct)
    {
        var actorList = string.Join(", ", actors.Select(a => a.Name));
        var reqSummary = string.Join("\n", requirements.Take(20).Select(r => $"- {r.Title}: {r.Description}"));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are an event-driven architecture expert. Given a domain, actors, and requirements,
                derive the canonical domain events that flow through the system.
                
                For each event:
                - Name: PascalCase event name (e.g. "OrderPlaced", "PatientAdmitted")
                - Source: The aggregate/service that produces it
                - Description: When and why this event is raised
                - PayloadFields: 3-5 key fields in the event payload
                
                Output ONLY a JSON array. No markdown.
                Example:
                [{"Name":"OrderPlaced","Source":"OrderService","Description":"Raised when a customer confirms an order","PayloadFields":["OrderId","CustomerId","TotalAmount","Items"]}]
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\nActors: {actorList}\n\nRequirements:\n{reqSummary}\n\nReturn domain events as JSON array.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var events = JsonSerializer.Deserialize<List<DomainEvent>>(
                    ExtractJson(response.Content),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (events is { Count: > 0 })
                    return events;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse domain events JSON — using empty list");
            }
        }

        return [];
    }

    // ═══════════════════════════════════════════════════════════════════
    // FALLBACK ER DIAGRAM
    // ═══════════════════════════════════════════════════════════════════

    private async Task<string> GenerateFallbackErDiagramAsync(
        string domain, string domainDesc, List<DomainActor> actors, CancellationToken ct)
    {
        var actorList = string.Join(", ", actors.Select(a => a.Name));

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = """
                You are a data modeling expert. Generate a Mermaid ER diagram for the given domain.
                Include 3-5 core entities with their key fields and relationships.
                
                Output ONLY the Mermaid erDiagram syntax. No markdown fences, no explanation.
                Example:
                erDiagram
                    ENTITY1 {
                        uuid Id PK
                        string Name
                    }
                    ENTITY2 {
                        uuid Id PK
                        uuid Entity1Id FK
                    }
                    ENTITY1 ||--o{ ENTITY2 : "has"
                """,
            UserPrompt = $"Domain: {domain}\nDescription: {domainDesc}\nActors: {actorList}\n\nGenerate a Mermaid ER diagram for this domain's core entities.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success && response.Content.Contains("erDiagram", StringComparison.OrdinalIgnoreCase))
            return response.Content.Trim();

        // Minimal fallback
        return """
            erDiagram
                ENTITY {
                    uuid Id PK
                    string Name
                    string Status
                }
                AUDIT_LOG {
                    uuid Id PK
                    uuid EntityId FK
                    datetime Timestamp
                    string Action
                }
                ENTITY ||--o{ AUDIT_LOG : "tracked by"
            """;
    }

    // ═══════════════════════════════════════════════════════════════════
    // AGENT PROMPT GENERATION
    // ═══════════════════════════════════════════════════════════════════

    private async Task<Dictionary<string, string>> GenerateAgentPromptsAsync(
        string domain, string domainDesc, DomainProfile profile, AgentContext context, CancellationToken ct)
    {
        var actorList = string.Join(", ", profile.Actors.Select(a => a.Name));
        var complianceList = string.Join(", ", profile.ComplianceFrameworks.Select(c => c.Name));
        var integrationList = string.Join(", ", profile.IntegrationPatterns.Select(i => i.Name));

        var agentNames = new[]
        {
            "BrdGenerator", "ApiDocumentation", "HipaaCompliance", "Soc2Compliance",
            "Security", "AccessControl", "Review", "Testing", "Integration",
            "Database", "ServiceLayer", "Application"
        };

        // ── Build per-agent learning blocks from historical learnings ──
        var learningBlocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agentName in agentNames)
        {
            if (Enum.TryParse<AgentType>(agentName, true, out var agentType))
            {
                var block = context.BuildLearningPromptBlock(agentType, 5);
                if (!string.IsNullOrEmpty(block))
                    learningBlocks[agentName] = block;
            }
        }

        var globalLearningSection = learningBlocks.Count > 0
            ? "\n\nHistorical learnings from previous pipeline runs (per-agent):\n" +
              string.Join("\n", learningBlocks.Select(kv => $"[{kv.Key}]: {kv.Value}")) +
              "\n\nIncorporate these lessons into each agent's system prompt to prevent recurring issues."
            : string.Empty;

        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = $$"""
                You are an AI system prompt engineer. Generate domain-tailored system prompts
                for each agent in a software development pipeline.
                
                Domain context:
                - Domain: {{domain}}
                - Description: {{domainDesc}}
                - Actors: {{actorList}}
                - Compliance: {{complianceList}}
                - Integration: {{integrationList}}
                - Sensitive fields: {{string.Join(", ", profile.SensitiveFieldPatterns.Take(10))}}
                
                For each agent, generate a system prompt (2-4 sentences) that:
                1. States the agent's role in the context of THIS specific domain
                2. References the actual actors/personas from this domain
                3. Mentions applicable compliance/integration requirements
                4. Uses domain-specific terminology, not generic placeholder terms
                5. Incorporates lessons learned from prior runs to avoid repeating known mistakes
                {{globalLearningSection}}
                
                Output ONLY a JSON object: { "AgentName": "system prompt string", ... }
                Generate prompts for these agents: {{string.Join(", ", agentNames)}}
                """,
            UserPrompt = $"Generate domain-specific system prompts for each agent. Return JSON object only.",
            Temperature = 0.3,
            RequestingAgent = Name,
            DomainHint = domain
        }, ct);

        if (response.Success)
        {
            try
            {
                var prompts = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    ExtractJson(response.Content));
                if (prompts is { Count: > 0 })
                    return new Dictionary<string, string>(prompts, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse agent prompts JSON — agents will use defaults");
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FALLBACK PROFILE (when LLM is unavailable)
    // ═══════════════════════════════════════════════════════════════════

    private static DomainProfile BuildFallbackProfile(string domain, string domainDesc)
    {
        return new DomainProfile
        {
            Domain = domain,
            DomainDescription = domainDesc,
            Actors = BuildFallbackActors(domain),
            ComplianceFrameworks =
            [
                new() { Name = "SOC2", IsUniversal = true, KeyClauses = ["CC6", "CC7", "CC8"] }
            ],
            IntegrationPatterns =
            [
                new() { Name = "REST/gRPC APIs", Applicability = "Always" },
                new() { Name = "Kafka Event Bus", Applicability = "Async events" }
            ],
            SensitiveFieldPatterns = ["Email", "Phone", "Address", "Password", "Secret", "Token", "DateOfBirth"],
            DomainRequirementTags = ["Core", "Admin", "Integration", "Security", "Reporting"],
            FallbackErDiagram = """
                erDiagram
                    ENTITY {
                        uuid Id PK
                        string Name
                        string Status
                    }
                """
        };
    }

    private static List<DomainActor> BuildFallbackActors(string domain)
    {
        var actors = new List<DomainActor>
        {
            new() { Name = "Administrator", Role = "System admin", Description = "Manages system configuration and user access" },
            new() { Name = "End User", Role = "Primary user", Description = "Uses core application features" },
            new() { Name = "System", Role = "Automated processes", Description = "Background jobs, schedulers, event processors" }
        };

        // Add one domain-hint actor
        if (!string.IsNullOrWhiteSpace(domain))
            actors.Add(new DomainActor { Name = "Domain Specialist", Role = $"{domain} specialist", Description = $"Domain expert for {domain}" });

        return actors;
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Extracts a JSON array or object from LLM output that may contain markdown fences.</summary>
    private static string ExtractJson(string text)
    {
        // Strip markdown code fences
        text = Regex.Replace(text, @"```(?:json)?\s*\n?", "").Trim();
        text = text.Replace("```", "").Trim();

        // Find first [ or { and last ] or }
        var firstBracket = text.IndexOfAny(['{', '[']);
        var lastBracket = text.LastIndexOfAny(['}', ']']);

        if (firstBracket >= 0 && lastBracket > firstBracket)
            return text[firstBracket..(lastBracket + 1)];

        return text;
    }
}
