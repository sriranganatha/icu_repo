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

        // HIPAA compliance
        if (user.Contains("hipaa") || user.Contains("phi"))
        {
            return """
                // AI-Generated: HIPAA Compliance Implementation
                // This code implements HIPAA Technical Safeguards (45 CFR § 164.312)
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
