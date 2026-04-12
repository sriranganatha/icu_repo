using System.Diagnostics;
using GNex.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Llm;

/// <summary>
/// Fallback LLM provider when no API key is configured. Returns structured
/// template-based responses so the pipeline runs end-to-end without external
/// dependencies. Agents receive "AI-assisted" guidance text that they embed
/// into generated artifacts as structured comments and implementation hints.
/// </summary>
public sealed class TemplateFallbackLlmProvider : ILlmProvider
{
    private readonly ILogger<TemplateFallbackLlmProvider> _logger;

    public string ProviderName => "TemplateFallback";
    public bool IsAvailable => true;

    public TemplateFallbackLlmProvider(ILogger<TemplateFallbackLlmProvider> logger)
        => _logger = logger;

    public Task<LlmResponse> GenerateAsync(LlmPrompt prompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogDebug("Template fallback generating for [{Agent}]", prompt.RequestingAgent);

        // Extract what kind of artifact the agent is asking for from the prompt
        var content = GenerateFromPromptHints(prompt);

        return Task.FromResult(new LlmResponse
        {
            Success = true,
            Content = content,
            Model = "template-fallback",
            PromptTokens = prompt.UserPrompt.Length / 4, // rough estimate
            CompletionTokens = content.Length / 4,
            Latency = sw.Elapsed
        });
    }

    private static string GenerateFromPromptHints(LlmPrompt prompt)
    {
        var user = prompt.UserPrompt.ToLowerInvariant();

        // BRD section generation — produce section-specific content
        if (prompt.RequestingAgent == "BrdUploadService" && user.Contains("sectiontype:"))
            return GenerateBrdSectionContent(prompt.UserPrompt, user);

        // Requirements Expander — produce pipe-delimited work items
        if (prompt.RequestingAgent == "Requirements Expander" && user.Contains("=== high-level requirements ==="))
            return GenerateRequirementsExpansion(prompt.UserPrompt);

        // Compliance
        if (user.Contains("compliance") || user.Contains("sensitive"))
        {
            return """
                // AI-Generated: Compliance Implementation
                // This code implements security and compliance safeguards
                //
                // Access Control (§164.312(a)): Role-based access with per-tenant isolation
                // Audit Controls (§164.312(b)): All PHI access logged with who/what/when/where
                // Integrity Controls (§164.312(c)): Hash verification on PHI modifications
                // Transmission Security (§164.312(e)): TLS 1.2+ enforced, AES-256 at rest
                //
                // PHI Categories tracked: Names, DOB, SSN, MRN, Insurance IDs, Addresses,
                // Phone numbers, Email, Biometric identifiers, Medical records, Lab results
                """;
        }

        // SOC2 compliance
        if (user.Contains("soc2") || user.Contains("soc 2"))
        {
            return """
                // AI-Generated: SOC 2 Type II Controls Implementation
                //
                // CC1 - Control Environment: Defined security policies, org structure
                // CC2 - Communication: Security awareness, incident reporting channels
                // CC3 - Risk Assessment: Automated vulnerability scanning, threat modeling
                // CC5 - Control Activities: Segregation of duties, change management
                // CC6 - Logical & Physical Access: MFA, RBAC, encrypted storage
                // CC7 - System Operations: Health monitoring, incident response, backup/DR
                // CC8 - Change Management: CI/CD gates, peer review, rollback capability
                // CC9 - Risk Mitigation: Business continuity, vendor risk management
                """;
        }

        // Security
        if (user.Contains("security") || user.Contains("owasp"))
        {
            return """
                // AI-Generated: Security Hardening
                // OWASP Top 10 mitigations applied:
                // A01-BrokenAccess: RBAC + tenant isolation + resource-level authorization
                // A02-Crypto: AES-256-GCM, bcrypt passwords, secure key management
                // A03-Injection: Parameterized queries, input validation, output encoding
                // A04-InsecureDesign: Threat modeling, secure defaults, least privilege
                // A05-Misconfiguration: Security headers, CORS policy, error handling
                // A06-VulnerableComponents: Dependency scanning, SCA in CI/CD
                // A07-AuthFailures: MFA, session management, brute-force protection
                // A08-Integrity: Signed artifacts, SBOM, supply chain verification
                // A09-Logging: Structured logging, audit trail, SIEM integration
                // A10-SSRF: URL allowlisting, network segmentation, egress controls
                """;
        }

        // Access control
        if (user.Contains("rbac") || user.Contains("access control") || user.Contains("authorization"))
        {
            return """
                // AI-Generated: Healthcare RBAC Policy
                // Roles: Physician, Nurse, Admin, Billing, LabTech, Pharmacist, Auditor, SysAdmin
                // Resource types: Patient, Encounter, Order, Result, Prescription, Claim
                // Actions: Read, Write, Delete, Approve, Prescribe, Discharge
                // Constraints: TenantId, FacilityId, DepartmentId, CareTeam membership
                // Emergency override: Break-the-glass with mandatory justification + audit
                """;
        }

        // Observability
        if (user.Contains("observability") || user.Contains("monitoring") || user.Contains("telemetry"))
        {
            return """
                // AI-Generated: Observability Stack Configuration
                // Metrics: Prometheus-compatible, per-service golden signals (latency, traffic, errors, saturation)
                // Traces: OpenTelemetry distributed tracing, correlation-id propagation
                // Logs: Structured JSON, Serilog sinks, ELK/Loki compatible
                // Alerts: SLA-based (p99 < 200ms for reads, < 500ms for writes)
                // Dashboards: Grafana templates for service health, tenant usage, PHI access audit
                """;
        }

        // Infrastructure
        if (user.Contains("infrastructure") || user.Contains("kubernetes") || user.Contains("docker") || user.Contains("helm"))
        {
            return """
                // AI-Generated: Infrastructure as Code
                // Container: Multi-stage Dockerfile, non-root user, distroless base
                // Orchestration: Kubernetes Deployment + HPA + PDB + NetworkPolicy
                // Helm chart: Values per environment (dev/staging/prod)
                // Database: PostgreSQL with connection pooling (PgBouncer), read replicas
                // Messaging: Kafka cluster with 3 brokers, topic auto-creation disabled
                // Secrets: External Secrets Operator → Azure Key Vault / AWS Secrets Manager
                """;
        }

        // API documentation
        if (user.Contains("openapi") || user.Contains("swagger") || user.Contains("api doc"))
        {
            return """
                // AI-Generated: OpenAPI 3.1 Specification
                // Endpoints follow RESTful conventions with FHIR-compatible resource naming
                // All endpoints require Bearer token + X-Tenant-Id header
                // Standard responses: 200, 201, 400, 401, 403, 404, 409, 422, 429, 500
                // PHI fields annotated with x-phi-classification extension
                // Pagination: cursor-based for large datasets, offset for small
                """;
        }

        // Generic fallback
        return $"""
            // AI-Generated: Template Fallback
            // Configure Llm:ApiKey in appsettings.json to enable full AI-powered generation.
            // Agent: {prompt.RequestingAgent}
            // Context snippets provided: {prompt.ContextSnippets.Count}
            // Prompt length: {prompt.UserPrompt.Length} chars
            """;
    }

    /// <summary>
    /// Generates pipe-delimited EPIC/STORY/USECASE/TASK lines by parsing the requirement
    /// summaries from the user prompt and producing a rich vertical-slice decomposition.
    /// </summary>
    private static string GenerateRequirementsExpansion(string rawPrompt)
    {
        var sb = new System.Text.StringBuilder();

        // Extract module name
        var module = "GEN";
        var moduleIdx = rawPrompt.IndexOf("Module:", StringComparison.OrdinalIgnoreCase);
        if (moduleIdx >= 0)
        {
            var afterModule = rawPrompt[(moduleIdx + 7)..];
            var eol = afterModule.IndexOfAny(['\r', '\n']);
            if (eol > 0) module = afterModule[..eol].Trim();
            if (module.Length > 10) module = module[..10];
            module = System.Text.RegularExpressions.Regex.Replace(module, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
            if (string.IsNullOrEmpty(module)) module = "GEN";
        }

        // Extract requirement IDs and titles from the structured prompt
        var reqSection = rawPrompt;
        var reqStart = rawPrompt.IndexOf("=== HIGH-LEVEL REQUIREMENTS", StringComparison.OrdinalIgnoreCase);
        var reqEnd = rawPrompt.IndexOf("=== ALREADY BUILT", StringComparison.OrdinalIgnoreCase);
        if (reqStart >= 0 && reqEnd > reqStart)
            reqSection = rawPrompt[reqStart..reqEnd];
        else if (reqStart >= 0)
            reqSection = rawPrompt[reqStart..];

        var reqLines = reqSection.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var requirements = new List<(string Id, string Title, string Description, string Services, List<string> Ac)>();

        string curId = "", curTitle = "", curDesc = "", curServices = "";
        var curAc = new List<string>();

        foreach (var line in reqLines)
        {
            if (line.StartsWith("- ") && line.Contains(':'))
            {
                // Save previous if exists
                if (!string.IsNullOrEmpty(curId))
                    requirements.Add((curId, curTitle, curDesc, curServices, new List<string>(curAc)));

                var colonIdx = line.IndexOf(':');
                curId = line[2..colonIdx].Trim();
                curTitle = line[(colonIdx + 1)..].Trim();
                curDesc = "";
                curServices = "";
                curAc = [];
            }
            else if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                curDesc = line["Description:".Length..].Trim();
            else if (line.StartsWith("Acceptance Criteria:", StringComparison.OrdinalIgnoreCase))
            {
                var acText = line["Acceptance Criteria:".Length..].Trim();
                if (!string.IsNullOrEmpty(acText))
                    curAc.AddRange(acText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (line.StartsWith("Affected Services:", StringComparison.OrdinalIgnoreCase))
                curServices = line["Affected Services:".Length..].Trim();
        }
        if (!string.IsNullOrEmpty(curId))
            requirements.Add((curId, curTitle, curDesc, curServices, new List<string>(curAc)));

        if (requirements.Count == 0)
        {
            // If we couldn't parse, create 1 synthetic requirement
            requirements.Add(($"REQ-{module}-001", $"{module} Core Functionality",
                "Core module functionality for the healthcare platform",
                "PatientService", ["Feature delivered and validated"]));
        }

        // Healthcare personas for vertical slicing
        var personas = new[] { "nurse", "doctor", "admin", "lab technician", "billing clerk" };

        var epicSeq = 0;
        foreach (var (reqId, reqTitle, reqDesc, reqSvcs, reqAc) in requirements)
        {
            epicSeq++;
            var epicId = $"E-{module}-{epicSeq:D3}";
            var services = string.IsNullOrEmpty(reqSvcs) || reqSvcs == "Unknown" ? "PatientService" : reqSvcs;
            var titleClean = reqTitle.Length > 80 ? reqTitle[..80] : reqTitle;
            var descClean = string.IsNullOrEmpty(reqDesc) ? titleClean : (reqDesc.Length > 200 ? reqDesc[..200] : reqDesc);

            // ── EPIC ──
            sb.AppendLine($"EPIC|{epicId}|{titleClean}|{descClean}|Improves clinical workflow efficiency and patient safety|" +
                $"Feature fully operational;All acceptance criteria verified;HIPAA compliance validated;Audit trail complete|" +
                $"Includes all CRUD operations, validation, and audit for this feature; excludes unrelated module refactors|" +
                $"2|{services}|");

            // ── USE CASE ──
            var ucId = $"UC-{module}-{epicSeq:D3}-01";
            sb.AppendLine($"USECASE|{ucId}|{epicId}|Execute {titleClean} Workflow|Nurse|" +
                $"User is authenticated and has appropriate role permissions|" +
                $"1. User navigates to the {titleClean} screen;2. System displays the relevant data list with search/filter;3. User selects or creates a record;4. System validates input against business rules;5. User confirms the action;6. System persists changes and creates audit log entry;7. System displays confirmation with updated data|" +
                $"Invalid input shows validation errors; Unauthorized user sees access denied; Network failure shows retry option|" +
                $"Data is persisted in database; Audit trail entry created; User sees confirmation|{services}");

            // ── USER STORIES (3-5 per epic, sliced by persona + operation) ──
            var storyOps = new[]
            {
                (Persona: "nurse", Action: $"search and view {titleClean.ToLowerInvariant()} records", Value: "I can quickly find patient information during care delivery", Pts: 3, Labels: "Frontend,API,Database"),
                (Persona: "doctor", Action: $"create and update {titleClean.ToLowerInvariant()} records", Value: "clinical documentation is accurate and up-to-date", Pts: 5, Labels: "Frontend,API,Database,Validation"),
                (Persona: "admin", Action: $"manage {titleClean.ToLowerInvariant()} configuration and permissions", Value: "the system meets our facility's operational policies", Pts: 3, Labels: "Frontend,API,Security"),
                (Persona: "system", Action: $"validate {titleClean.ToLowerInvariant()} data integrity and generate audit trails", Value: "regulatory compliance (HIPAA) is maintained automatically", Pts: 2, Labels: "API,Database,Security"),
                (Persona: "billing clerk", Action: $"export and report on {titleClean.ToLowerInvariant()} data", Value: "revenue cycle operations run accurately and on time", Pts: 3, Labels: "Frontend,API,Reporting")
            };

            var storySeq = 0;
            foreach (var op in storyOps)
            {
                storySeq++;
                var storyId = $"US-{module}-{epicSeq:D3}-{storySeq:D2}";
                var acList = $"Given the {op.Persona} is authenticated, when they {op.Action}, then the system processes the request and displays results within 2 seconds;" +
                    $"Given invalid input is submitted, when the system validates, then clear error messages guide the user to correct the issue;" +
                    $"Given the operation completes, when the audit service records the action, then a complete audit trail entry exists with who/what/when";

                sb.AppendLine($"STORY|{storyId}|{epicId}|As a {op.Persona}, I want to {op.Action} so that {op.Value}|" +
                    $"{acList}|{op.Pts}|{op.Labels}|2|{services}||" +
                    $"Implement end-to-end {op.Action} with validation, error handling, and audit trail support");

                // ── TASKS (6 per story) ──
                var contractTaskId = $"T-{module}-{epicSeq:D3}-{storySeq:D2}-CONTRACT";
                var dbTaskId = $"T-{module}-{epicSeq:D3}-{storySeq:D2}-DB";
                var svcTaskId = $"T-{module}-{epicSeq:D3}-{storySeq:D2}-SVC";
                var apiTaskId = $"T-{module}-{epicSeq:D3}-{storySeq:D2}-API";
                var intTestId = $"T-{module}-{epicSeq:D3}-{storySeq:D2}-ITEST";
                var e2eTestId = $"T-{module}-{epicSeq:D3}-{storySeq:D2}-E2E";

                // Task 1: API Contract
                sb.AppendLine($"TASK|{contractTaskId}|{storyId}|[{contractTaskId}] Define API contract for {op.Action}|" +
                    $"Define OpenAPI specification with request/response DTOs, routes, status codes, and validation schemas|" +
                    $"Use OpenAPI 3.1 spec; Include X-Tenant-Id header; Define 200, 400, 401, 403, 404, 422 responses|" +
                    $"OpenAPI spec reviewed and approved;DTO classes generated;Contract tests written|" +
                    $"api,contract|2|{services}|" +
                    $"Define DTOs: request with required fields and validation attributes, response with pagination support. Routes follow REST conventions.");

                // Task 2: Database
                sb.AppendLine($"TASK|{dbTaskId}|{storyId}|[{dbTaskId}] Implement database entities and migrations|" +
                    $"Create EF Core entities, DbContext configuration, indexes, and migration for the data model|" +
                    $"Use PostgreSQL-friendly types; Add tenant isolation (TenantId FK); Index lookup columns; Add soft-delete support|" +
                    $"Migration runs without errors;Indexes verified;Seed data present;Rollback tested|" +
                    $"database|2|{services}|" +
                    $"Entity with Id (text PK), TenantId, CreatedAt, UpdatedAt, IsActive, plus domain-specific fields. Indexes on TenantId and lookup columns.");

                // Task 3: Service layer
                sb.AppendLine($"TASK|{svcTaskId}|{storyId}|[{svcTaskId}] Implement service layer with validation and business logic|" +
                    $"Build service class with CRUD operations, FluentValidation rules, domain events, and multi-tenant filtering|" +
                    $"Inject IRepository; Use FluentValidation; Emit domain events for audit; All queries filtered by TenantId|" +
                    $"Unit tests passing (>80% coverage);Validation rules tested;Domain events emitted;Exception handling complete|" +
                    $"service|2|{services}|" +
                    $"Service implements I{module}Service interface. Methods: GetByIdAsync, ListAsync (paginated), CreateAsync, UpdateAsync, SoftDeleteAsync. All operations log to audit trail.");

                // Task 4: API endpoint
                sb.AppendLine($"TASK|{apiTaskId}|{storyId}|[{apiTaskId}] Build API endpoint wiring service to HTTP|" +
                    $"Create ASP.NET Core controller with route mapping, model binding, authorization attributes, and error responses|" +
                    $"Use [Authorize] with role policy; Map service exceptions to HTTP status codes; Add response caching for reads|" +
                    $"All routes return correct status codes;Authorization tested;Swagger documentation complete;Rate limiting configured|" +
                    $"api|2|{services}|" +
                    $"Controller with GET (list+detail), POST, PUT, DELETE endpoints. Use ProblemDetails for errors. Add [ProducesResponseType] for Swagger.");

                // Task 5: Integration tests
                sb.AppendLine($"TASK|{intTestId}|{storyId}|[{intTestId}] Write integration tests for API endpoints|" +
                    $"Create integration tests covering happy path, validation errors, auth failures, not-found scenarios, and concurrent access|" +
                    $"Use WebApplicationFactory; Test with real DB (in-memory or TestContainers); Cover auth bypass and tenant isolation|" +
                    $"Happy path tested;Validation error responses tested;401/403 tested;404 tested;Concurrent writes tested|" +
                    $"testing|2|{services}|" +
                    $"Test scenarios: create+read round-trip, duplicate prevention, invalid input (422), unauthorized (401), forbidden (403), not found (404), optimistic concurrency.");

                // Task 6: E2E test
                sb.AppendLine($"TASK|{e2eTestId}|{storyId}|[{e2eTestId}] Write E2E test for full {op.Persona} flow|" +
                    $"Create end-to-end test simulating the complete user journey from login through {op.Action} to verification|" +
                    $"Use Playwright or similar; Test against staging-like environment; Verify database state after operations|" +
                    $"Complete user journey tested;Data persistence verified;Audit log entries verified;Performance within SLA|" +
                    $"testing,e2e|3|{services}|" +
                    $"E2E flow: authenticate as {op.Persona} -> navigate to feature -> perform {op.Action} -> verify UI feedback -> verify DB records -> verify audit log entry.");
            }
        }

        return sb.ToString();
    }

    private static string GenerateBrdSectionContent(string rawPrompt, string lowerPrompt)
    {
        // Extract SectionType, SectionTitle, and Prompt from the structured prompt
        var sectionType = ExtractPromptField(rawPrompt, "SectionType:");
        var sectionTitle = ExtractPromptField(rawPrompt, "SectionTitle:");
        var templatePrompt = ExtractMultiLineField(rawPrompt, "Prompt:", "The requirements corpus below");
        if (string.IsNullOrWhiteSpace(templatePrompt))
            templatePrompt = ExtractMultiLineField(rawPrompt, "Prompt:", "Requirements corpus:");
        if (string.IsNullOrWhiteSpace(templatePrompt))
            templatePrompt = ExtractPromptField(rawPrompt, "Prompt:");

        // Extract corpus lines to weave into the section
        var corpusStart = rawPrompt.IndexOf("Requirements corpus:", StringComparison.OrdinalIgnoreCase);
        var corpusLines = new List<string>();
        if (corpusStart >= 0)
        {
            var corpus = rawPrompt[(corpusStart + "Requirements corpus:".Length)..];
            var rulesIdx = corpus.IndexOf("Output rules:", StringComparison.OrdinalIgnoreCase);
            if (rulesIdx > 0) corpus = corpus[..rulesIdx];
            corpusLines = corpus
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length >= 10 && !l.StartsWith("//"))
                .ToList();
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## {sectionTitle}");
        sb.AppendLine();

        // Use the template prompt as the section description
        if (!string.IsNullOrWhiteSpace(templatePrompt))
            sb.AppendLine($"*{templatePrompt}*");
        else
            sb.AppendLine($"*Analysis for the {sectionTitle} section based on uploaded requirements.*");
        sb.AppendLine();

        // Group corpus evidence into sub-topics by detecting heading-like lines
        if (corpusLines.Count > 0)
        {
            sb.AppendLine("### Key Requirements from Source Files");
            sb.AppendLine();

            string? currentGroup = null;
            foreach (var line in corpusLines)
            {
                var clean = line.TrimStart('-', '*', ' ', '\t', '#');
                if (clean.Length < 8) continue;

                // Detect heading-like lines (start with ## or are all-caps short lines)
                if (line.TrimStart().StartsWith('#') || (clean.Length < 80 && clean == clean.ToUpperInvariant() && !clean.Contains(' ')))
                {
                    if (currentGroup != clean)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"**{clean}**");
                        currentGroup = clean;
                    }
                }
                else
                {
                    sb.AppendLine($"- {clean}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("*Generated offline — set an API key in settings to enable full AI-powered generation.*");

        return sb.ToString();
    }

    private static string ExtractPromptField(string prompt, string fieldName)
    {
        var idx = prompt.IndexOf(fieldName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;
        var start = idx + fieldName.Length;
        var endIdx = prompt.IndexOfAny(['\r', '\n'], start);
        return endIdx < 0 ? prompt[start..].Trim() : prompt[start..endIdx].Trim();
    }

    private static string ExtractMultiLineField(string prompt, string fieldStart, string fieldEnd)
    {
        var startIdx = prompt.IndexOf(fieldStart, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return string.Empty;
        var contentStart = startIdx + fieldStart.Length;
        var endIdx = prompt.IndexOf(fieldEnd, contentStart, StringComparison.OrdinalIgnoreCase);
        if (endIdx < 0) return string.Empty;
        return prompt[contentStart..endIdx].Trim();
    }
}
