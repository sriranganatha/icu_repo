using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Compliance;

/// <summary>
/// AI-powered SOC 2 Type II compliance agent. Generates controls evidence,
/// change management policies, system monitoring configuration, and continuous
/// compliance validation across all Trust Services Criteria (CC1-CC9).
/// </summary>
public sealed class Soc2ComplianceAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<Soc2ComplianceAgent> _logger;

    public AgentType Type => AgentType.Soc2Compliance;
    public string Name => "SOC 2 Compliance Agent";
    public string Description => "Generates SOC 2 Type II controls: change management gates, access reviews, incident response, backup/DR, and continuous monitoring.";

    public Soc2ComplianceAgent(ILlmProvider llm, ILogger<Soc2ComplianceAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("Soc2ComplianceAgent starting — AI-powered SOC 2 controls generation");

        var findings = new List<ReviewFinding>();
        var artifacts = new List<CodeArtifact>();

        try
        {
            // Scan for SOC 2 gaps
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Scanning for SOC 2 Type II control gaps — change management (CC8), access controls (CC6), system operations (CC7)");
            findings.AddRange(ScanChangeManagement(context));
            findings.AddRange(ScanAccessControls(context));
            findings.AddRange(ScanSystemOperations(context));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Found {findings.Count} SOC 2 control gaps. AI-generating compliance artifacts...");

            // Generate SOC 2 compliance artifacts
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating change management gate — PR approval workflow, test gate enforcement");
            artifacts.Add(await GenerateChangeManagementGate(ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating access review service — quarterly access certifications, orphan account detection");
            artifacts.Add(await GenerateAccessReviewService(ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating incident response plan — severity classification, escalation, post-mortem template");
            artifacts.Add(await GenerateIncidentResponsePlan(ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating backup & DR policy — RPO/RTO targets, failover procedures, restore testing");
            artifacts.Add(await GenerateBackupDrPolicy(context, ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating SOC 2 control matrix — mapping CC1-CC9 criteria to implemented controls");
            artifacts.Add(GenerateSoc2ControlMatrix());

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Dispatch SOC 2 findings as feedback to responsible code-gen agents
            if (findings.Count > 0)
                context.DispatchFindingsAsFeedback(Type, findings);

            // Notify code-gen agents about SOC 2 compliance gaps
            if (findings.Count > 0)
            {
                context.WriteFeedback(AgentType.Deploy, Type, $"SOC 2: {findings.Count} control gaps — ensure change management gates, backup/DR policies in deployment pipeline.");
                context.WriteFeedback(AgentType.Application, Type, $"SOC 2: Wire access review and incident response into application middleware — {findings.Count} gaps found.");
                context.WriteFeedback(AgentType.Infrastructure, Type, $"SOC 2: {findings.Count} infrastructure compliance gaps — backup/DR, monitoring, access controls.");
            }

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"SOC 2 Agent: {findings.Count} control gaps, {artifacts.Count} compliance artifacts (AI: {_llm.ProviderName})",
                Artifacts = artifacts, Findings = findings,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "SOC 2 controls generated",
                    Body = $"Change management, access review, incident response, DR policy, control matrix. {findings.Count} gaps." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "Soc2ComplianceAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static List<ReviewFinding> ScanChangeManagement(AgentContext context)
    {
        var findings = new List<ReviewFinding>();
        // CC8: Check for CI/CD artifacts
        if (!context.Artifacts.Any(a => a.FileName.Contains("pipeline", StringComparison.OrdinalIgnoreCase) ||
                                        a.FileName.Contains("ci", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new ReviewFinding
            {
                Severity = ReviewSeverity.Warning, Category = "SOC2-CC8",
                Message = "No CI/CD pipeline definition found.",
                Suggestion = "Add GitHub Actions / Azure DevOps pipeline with required reviewers, test gates, and deployment approvals."
            });
        }
        return findings;
    }

    private static List<ReviewFinding> ScanAccessControls(AgentContext context)
    {
        var findings = new List<ReviewFinding>();
        // CC6: Check for RBAC/auth patterns
        var hasAuth = context.Artifacts.Any(a => a.Content.Contains("Authorize") || a.Content.Contains("RBAC") || a.Content.Contains("RequireAuthorization"));
        if (!hasAuth)
        {
            findings.Add(new ReviewFinding
            {
                Severity = ReviewSeverity.Error, Category = "SOC2-CC6",
                Message = "No authorization enforcement found in any artifact.",
                Suggestion = "Implement RBAC with role-based endpoint protection and periodic access reviews."
            });
        }
        return findings;
    }

    private static List<ReviewFinding> ScanSystemOperations(AgentContext context)
    {
        var findings = new List<ReviewFinding>();
        // CC7: Check for health checks / monitoring
        var hasMonitoring = context.Artifacts.Any(a =>
            a.Content.Contains("HealthCheck") || a.Content.Contains("IHealthCheck") ||
            a.FileName.Contains("Health", StringComparison.OrdinalIgnoreCase));
        if (!hasMonitoring)
        {
            findings.Add(new ReviewFinding
            {
                Severity = ReviewSeverity.Warning, Category = "SOC2-CC7",
                Message = "No health check or monitoring artifacts found.",
                Suggestion = "Add IHealthCheck implementations for DB, Kafka, and external service dependencies."
            });
        }
        return findings;
    }

    private async Task<CodeArtifact> GenerateChangeManagementGate(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a SOC 2 compliance expert. Generate C# code for a change management gate service.",
            UserPrompt = "Generate a ChangeManagementGate class that validates: 1) All changes have a ticket/PR, 2) Required peer review, 3) Automated tests passed, 4) Security scan passed, 5) Approval from change advisory board for production. Include ChangeRequest record and IChangeManagementGate interface. Namespace: GNex.SharedKernel.Compliance.Soc2.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "GNex.SharedKernel/Compliance/Soc2/ChangeManagementGate.cs",
            FileName = "ChangeManagementGate.cs",
            Namespace = "GNex.SharedKernel.Compliance.Soc2",
            ProducedBy = AgentType.Soc2Compliance,
            TracedRequirementIds = ["SOC2-CC8"],
            Content = response.Success ? response.Content : GenerateChangeGateFallback()
        };
    }

    private async Task<CodeArtifact> GenerateAccessReviewService(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a SOC 2 compliance expert. Generate C# code for periodic access reviews.",
            UserPrompt = "Generate an AccessReviewService that: 1) Lists all user→role mappings, 2) Flags dormant accounts (>90 days), 3) Flags over-privileged users, 4) Generates quarterly access review report. Namespace: GNex.SharedKernel.Compliance.Soc2.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "GNex.SharedKernel/Compliance/Soc2/AccessReviewService.cs",
            FileName = "AccessReviewService.cs",
            Namespace = "GNex.SharedKernel.Compliance.Soc2",
            ProducedBy = AgentType.Soc2Compliance,
            TracedRequirementIds = ["SOC2-CC6"],
            Content = response.Success ? response.Content : """
                namespace GNex.SharedKernel.Compliance.Soc2;

                public sealed record AccessReviewEntry
                {
                    public string UserId { get; init; } = string.Empty;
                    public string Role { get; init; } = string.Empty;
                    public DateTimeOffset LastActiveAt { get; init; }
                    public bool IsDormant => DateTimeOffset.UtcNow - LastActiveAt > TimeSpan.FromDays(90);
                    public string[] Permissions { get; init; } = [];
                }

                public interface IAccessReviewService
                {
                    Task<List<AccessReviewEntry>> GetAllMappingsAsync(string tenantId, CancellationToken ct = default);
                    Task<List<AccessReviewEntry>> GetDormantAccountsAsync(string tenantId, CancellationToken ct = default);
                    Task<List<AccessReviewEntry>> GetOverPrivilegedAsync(string tenantId, CancellationToken ct = default);
                    Task<string> GenerateQuarterlyReportAsync(string tenantId, CancellationToken ct = default);
                }
                """
        };
    }

    private async Task<CodeArtifact> GenerateIncidentResponsePlan(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a SOC 2 compliance expert for enterprise software. Generate a C# incident response service.",
            UserPrompt = "Generate an IncidentResponseService with: 1) ReportIncident, 2) ClassifySeverity (P1-P4), 3) EscalationPath per severity, 4) PostIncidentReview. Include IncidentRecord entity. Namespace: GNex.SharedKernel.Compliance.Soc2.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "GNex.SharedKernel/Compliance/Soc2/IncidentResponseService.cs",
            FileName = "IncidentResponseService.cs",
            Namespace = "GNex.SharedKernel.Compliance.Soc2",
            ProducedBy = AgentType.Soc2Compliance,
            TracedRequirementIds = ["SOC2-CC7"],
            Content = response.Success ? response.Content : """
                namespace GNex.SharedKernel.Compliance.Soc2;

                public enum IncidentSeverity { P1_Critical, P2_High, P3_Medium, P4_Low }

                public sealed record IncidentRecord
                {
                    public string Id { get; init; } = Guid.NewGuid().ToString("N");
                    public string TenantId { get; init; } = string.Empty;
                    public string Title { get; init; } = string.Empty;
                    public string Description { get; init; } = string.Empty;
                    public IncidentSeverity Severity { get; init; }
                    public string ReportedBy { get; init; } = string.Empty;
                    public DateTimeOffset ReportedAt { get; init; } = DateTimeOffset.UtcNow;
                    public DateTimeOffset? ResolvedAt { get; set; }
                    public string RootCause { get; set; } = string.Empty;
                    public string Remediation { get; set; } = string.Empty;
                }

                public interface IIncidentResponseService
                {
                    IncidentRecord ReportIncident(string tenantId, string title, string description);
                    IncidentSeverity ClassifySeverity(IncidentRecord incident);
                    string[] GetEscalationPath(IncidentSeverity severity);
                    Task CompletePostIncidentReviewAsync(IncidentRecord incident, string rootCause, string remediation, CancellationToken ct = default);
                }
                """
        };
    }

    private async Task<CodeArtifact> GenerateBackupDrPolicy(AgentContext context, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a SOC 2 compliance expert. Generate C# backup and disaster recovery configuration.",
            UserPrompt = "Generate a BackupDrPolicy class with: RPO/RTO targets per service tier, backup schedule configs, failover procedures, and recovery test schedule. Namespace: GNex.SharedKernel.Compliance.Soc2.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        // Build dynamic service tier mapping for the fallback template
        var services = ServiceCatalogResolver.GetServices(context);
        var tierEntries = services.Select(s =>
        {
            var tier = s.DependsOn.Length > 2 || s.Entities.Length > 3 ? "Critical" :
                       s.Entities.Length > 1 ? "Standard" : "NonCritical";
            return $"                        [\"{s.Name}\"] = \"{tier}\",";
        });
        var tierMappingBlock = string.Join("\n", tierEntries);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "GNex.SharedKernel/Compliance/Soc2/BackupDrPolicy.cs",
            FileName = "BackupDrPolicy.cs",
            Namespace = "GNex.SharedKernel.Compliance.Soc2",
            ProducedBy = AgentType.Soc2Compliance,
            TracedRequirementIds = ["SOC2-CC7", "SOC2-CC9"],
            Content = response.Success ? response.Content : $$"""
                namespace GNex.SharedKernel.Compliance.Soc2;

                public static class BackupDrPolicy
                {
                    public sealed record ServiceTier(string Name, TimeSpan Rpo, TimeSpan Rto, string BackupSchedule);

                    public static readonly ServiceTier[] Tiers =
                    [
                        new("Critical",    TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), "Continuous WAL shipping"),
                        new("Standard",    TimeSpan.FromHours(1),    TimeSpan.FromHours(2),    "Hourly snapshots"),
                        new("NonCritical", TimeSpan.FromHours(24),   TimeSpan.FromHours(8),    "Daily snapshots"),
                    ];

                    public static readonly Dictionary<string, string> ServiceTierMapping = new()
                    {
                {{tierMappingBlock}}
                    };

                    public static string RecoveryTestSchedule => "Quarterly DR test with full failover simulation";
                }
                """
        };
    }

    private static CodeArtifact GenerateSoc2ControlMatrix() => new()
    {
        Layer = ArtifactLayer.Compliance,
        RelativePath = "GNex.SharedKernel/Compliance/Soc2/Soc2ControlMatrix.cs",
        FileName = "Soc2ControlMatrix.cs",
        Namespace = "GNex.SharedKernel.Compliance.Soc2",
        ProducedBy = AgentType.Soc2Compliance,
        TracedRequirementIds = ["SOC2-CC1", "SOC2-CC5", "SOC2-CC6", "SOC2-CC7", "SOC2-CC8", "SOC2-CC9"],
        Content = """
            namespace GNex.SharedKernel.Compliance.Soc2;

            public sealed record Soc2Control(string Id, string Criteria, string Description, string Evidence, string Owner);

            public static class Soc2ControlMatrix
            {
                public static readonly Soc2Control[] Controls =
                [
                    new("CC1.1", "CC1", "Security policies documented and communicated", "Policy repository + signed acknowledgments", "CISO"),
                    new("CC5.1", "CC5", "Segregation of duties in change management", "PR approvals, deploy gates, no self-approve", "Engineering Lead"),
                    new("CC5.2", "CC5", "Least privilege access", "RBAC audit logs, quarterly access reviews", "Security Team"),
                    new("CC6.1", "CC6", "Logical access controls", "MFA, session timeout, API key rotation", "Platform Team"),
                    new("CC6.2", "CC6", "Encryption in transit and at rest", "TLS 1.2+ certs, AES-256 DB encryption", "Platform Team"),
                    new("CC7.1", "CC7", "System monitoring and alerting", "Prometheus metrics, PagerDuty alerts", "SRE"),
                    new("CC7.2", "CC7", "Incident response procedures", "Runbooks, post-incident reviews, SLA tracking", "SRE"),
                    new("CC7.3", "CC7", "Backup and recovery", "Automated backups, quarterly DR tests", "DBA"),
                    new("CC8.1", "CC8", "Change management process", "PR reviews, CI/CD gates, staging validation", "Engineering Lead"),
                    new("CC8.2", "CC8", "Testing before deployment", "Unit/integration/E2E tests, coverage gates", "QA"),
                    new("CC9.1", "CC9", "Risk assessment", "Annual pentest, vulnerability scans, threat model", "Security Team"),
                    new("CC9.2", "CC9", "Vendor risk management", "Third-party security questionnaires", "Compliance"),
                ];
            }
            """
    };

    private static string GenerateChangeGateFallback() => """
        namespace GNex.SharedKernel.Compliance.Soc2;

        public sealed record ChangeRequest
        {
            public string Id { get; init; } = Guid.NewGuid().ToString("N");
            public string TicketId { get; init; } = string.Empty;
            public string PullRequestUrl { get; init; } = string.Empty;
            public string RequestedBy { get; init; } = string.Empty;
            public string[] Reviewers { get; init; } = [];
            public bool TestsPassed { get; init; }
            public bool SecurityScanPassed { get; init; }
            public bool CabApproved { get; init; }
            public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
        }

        public interface IChangeManagementGate
        {
            bool ValidateChange(ChangeRequest request);
            string[] GetBlockingReasons(ChangeRequest request);
        }

        public sealed class ChangeManagementGate : IChangeManagementGate
        {
            public bool ValidateChange(ChangeRequest request)
                => GetBlockingReasons(request).Length == 0;

            public string[] GetBlockingReasons(ChangeRequest request)
            {
                var reasons = new List<string>();
                if (string.IsNullOrEmpty(request.TicketId)) reasons.Add("No ticket/issue linked");
                if (string.IsNullOrEmpty(request.PullRequestUrl)) reasons.Add("No PR created");
                if (request.Reviewers.Length == 0) reasons.Add("No peer reviewers assigned");
                if (!request.TestsPassed) reasons.Add("Automated tests not passed");
                if (!request.SecurityScanPassed) reasons.Add("Security scan not passed");
                if (!request.CabApproved) reasons.Add("CAB approval required for production");
                return reasons.ToArray();
            }
        }
        """;
}
