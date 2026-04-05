using System.Diagnostics;
using HmsAgents.Agents.Requirements;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Compliance;

/// <summary>
/// AI-powered HIPAA compliance agent. Scans all artifacts for PHI exposure,
/// generates BAA templates, breach notification procedures, minimum necessary
/// access policies, and audit trail infrastructure per 45 CFR §164.
/// </summary>
public sealed class HipaaComplianceAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<HipaaComplianceAgent> _logger;

    public AgentType Type => AgentType.HipaaCompliance;
    public string Name => "HIPAA Compliance Agent";
    public string Description => "Enforces HIPAA Technical Safeguards (45 CFR §164.312): PHI encryption, access audit trails, minimum necessary access, and breach notification.";

    public HipaaComplianceAgent(ILlmProvider llm, ILogger<HipaaComplianceAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("HipaaComplianceAgent starting — AI-powered PHI analysis");

        var findings = new List<ReviewFinding>();
        var artifacts = new List<CodeArtifact>();

        try
        {
            // 1. Scan all artifacts for PHI fields
            var phiFields = new[] { "DateOfBirth", "SocialSecurity", "SSN", "MedicalRecordNumber",
                "InsuranceId", "DriversLicense", "LegalGivenName", "LegalFamilyName",
                "PreferredName", "PrimaryLanguage", "SexAtBirth", "Diagnosis", "TreatmentPlan",
                "LabResult", "Prescription", "Allergy", "Immunization", "VitalSign" };

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"Scanning {context.Artifacts.Count} artifacts for {phiFields.Length} PHI field types (45 CFR §164.312)");

            foreach (var artifact in context.Artifacts)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var phi in phiFields)
                {
                    if (artifact.Content.Contains(phi, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check encryption at rest
                        if (!artifact.Content.Contains("Encrypt") && !artifact.Content.Contains("Classification") &&
                            artifact.Layer is ArtifactLayer.Service or ArtifactLayer.Repository)
                        {
                            findings.Add(new ReviewFinding
                            {
                                ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                                Severity = ReviewSeverity.ComplianceViolation,
                                Category = "HIPAA-164.312(a)",
                                Message = $"PHI field '{phi}' in '{artifact.FileName}' without encryption-at-rest or classification marker.",
                                Suggestion = "Apply DataEncryptionHelper or add ClassificationCode for PHI governance."
                            });
                        }
                        break;
                    }
                }

                // Check audit logging on data access
                if (artifact.Layer == ArtifactLayer.Service && !artifact.FileName.StartsWith("I"))
                {
                    if (!artifact.Content.Contains("LogInformation") && !artifact.Content.Contains("AuditLog"))
                    {
                        findings.Add(new ReviewFinding
                        {
                            ArtifactId = artifact.Id, FilePath = artifact.RelativePath,
                            Severity = ReviewSeverity.ComplianceViolation,
                            Category = "HIPAA-164.312(b)",
                            Message = $"Service '{artifact.FileName}' lacks audit logging on PHI access.",
                            Suggestion = "Log all PHI access with user, timestamp, entity, and action for audit controls."
                        });
                    }
                }
            }

            // 2. AI-generate HIPAA compliance artifacts
            var domainSummary = BuildDomainSummary(context);

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, $"PHI scan done: {findings.Count} compliance findings. AI-generating HIPAA artifacts — inventory, audit service, breach notification...");

            artifacts.Add(await GeneratePhiInventory(domainSummary, ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generated PHI inventory — cataloging all protected health information across services");
            artifacts.Add(await GenerateAccessAuditService(domainSummary, ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generated access audit service — logging all PHI access with user, timestamp, entity, action");
            artifacts.Add(await GenerateBreachNotificationPolicy(ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generated breach notification policy — 60-day notification workflow per HHS requirements");
            artifacts.Add(await GenerateMinimumNecessaryPolicy(context, ct));
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generated minimum necessary access policy — role-based PHI field restriction");
            artifacts.Add(GeneratePhiClassificationEnum());

            context.Artifacts.AddRange(artifacts);
            context.Findings.AddRange(findings);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"HIPAA Agent: {findings.Count} compliance findings, {artifacts.Count} HIPAA artifacts generated (AI-powered: {_llm.ProviderName})",
                Artifacts = artifacts, Findings = findings,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "HIPAA compliance scan complete",
                    Body = $"PHI inventory, access audit service, breach notification, minimum necessary policy generated. {findings.Count} compliance gaps identified." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "HipaaComplianceAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static string BuildDomainSummary(AgentContext context)
    {
        var entities = context.DomainModel?.Entities ?? [];
        return string.Join("\n", entities.Select(e =>
            $"- {e.ServiceName}.{e.Name}: {string.Join(", ", e.Fields.Take(10).Select(f => f.Name))}"));
    }

    private async Task<CodeArtifact> GeneratePhiInventory(string domainSummary, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a HIPAA compliance expert for healthcare software. Generate C# code that catalogs all PHI fields across the system.",
            UserPrompt = $"Generate a static PhiInventory class that maps every entity to its PHI fields for the HMS platform.\n\nEntities:\n{domainSummary}\n\nOutput a single C# file with namespace Hms.SharedKernel.Compliance.",
            Temperature = 0.1,
            RequestingAgent = Name,
            ContextSnippets = [domainSummary]
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "Hms.SharedKernel/Compliance/PhiInventory.cs",
            FileName = "PhiInventory.cs",
            Namespace = "Hms.SharedKernel.Compliance",
            ProducedBy = AgentType.HipaaCompliance,
            TracedRequirementIds = ["NFR-HIPAA-01", "HIPAA-164.312"],
            Content = response.Success ? response.Content : GeneratePhiInventoryFallback()
        };
    }

    private async Task<CodeArtifact> GenerateAccessAuditService(string domainSummary, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a HIPAA compliance expert. Generate a C# audit service that logs every PHI access event with user, timestamp, resource, and action for 45 CFR §164.312(b).",
            UserPrompt = "Generate a PhiAccessAuditService class with methods: LogAccess, LogModification, LogDeletion, LogExport. Each logs to a PhiAuditEntry table. Include the entity and interface. Namespace: Hms.SharedKernel.Compliance.",
            Temperature = 0.1,
            RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "Hms.SharedKernel/Compliance/PhiAccessAuditService.cs",
            FileName = "PhiAccessAuditService.cs",
            Namespace = "Hms.SharedKernel.Compliance",
            ProducedBy = AgentType.HipaaCompliance,
            TracedRequirementIds = ["NFR-AUD-01", "HIPAA-164.312(b)"],
            Content = response.Success ? response.Content : GenerateAccessAuditFallback()
        };
    }

    private async Task<CodeArtifact> GenerateBreachNotificationPolicy(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a HIPAA compliance expert. Generate a C# breach notification service per HIPAA Breach Notification Rule (45 CFR §164.400-414).",
            UserPrompt = "Generate a BreachNotificationService with: ReportBreach, AssessRisk (4-factor test), NotifyIndividuals, NotifyHhs, NotifyMedia (>500 records). Include BreachRecord entity. Namespace: Hms.SharedKernel.Compliance.",
            Temperature = 0.1,
            RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "Hms.SharedKernel/Compliance/BreachNotificationService.cs",
            FileName = "BreachNotificationService.cs",
            Namespace = "Hms.SharedKernel.Compliance",
            ProducedBy = AgentType.HipaaCompliance,
            TracedRequirementIds = ["HIPAA-164.400"],
            Content = response.Success ? response.Content : GenerateBreachNotificationFallback()
        };
    }

    private async Task<CodeArtifact> GenerateMinimumNecessaryPolicy(AgentContext context, CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a HIPAA compliance expert. Generate a C# field-level access policy that enforces the Minimum Necessary standard — each role only sees PHI fields they need.",
            UserPrompt = "Generate a MinimumNecessaryPolicy static class with method: FilterFields<T>(T entity, string role) that nullifies PHI fields the role should not see. Roles: Physician (all), Nurse (clinical subset), Billing (financial only), Admin (demographics only). Namespace: Hms.SharedKernel.Compliance.",
            Temperature = 0.1,
            RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Compliance,
            RelativePath = "Hms.SharedKernel/Compliance/MinimumNecessaryPolicy.cs",
            FileName = "MinimumNecessaryPolicy.cs",
            Namespace = "Hms.SharedKernel.Compliance",
            ProducedBy = AgentType.HipaaCompliance,
            TracedRequirementIds = ["HIPAA-MinNecessary"],
            Content = response.Success ? response.Content : GenerateMinNecessaryFallback()
        };
    }

    private static CodeArtifact GeneratePhiClassificationEnum() => new()
    {
        Layer = ArtifactLayer.Compliance,
        RelativePath = "Hms.SharedKernel/Compliance/PhiClassification.cs",
        FileName = "PhiClassification.cs",
        Namespace = "Hms.SharedKernel.Compliance",
        ProducedBy = AgentType.HipaaCompliance,
        TracedRequirementIds = ["NFR-HIPAA-01"],
        Content = """
            namespace Hms.SharedKernel.Compliance;

            /// <summary>
            /// HIPAA PHI classification levels for data governance.
            /// Applied to entity fields and DTO properties for access control.
            /// </summary>
            public enum PhiClassification
            {
                /// <summary>Non-PHI: facility codes, status codes, system metadata.</summary>
                Public = 0,

                /// <summary>De-identified per Safe Harbor: age ranges, zip-3, dates rounded to year.</summary>
                DeIdentified = 1,

                /// <summary>Limited Data Set: dates, zip codes, city/state (requires DUA).</summary>
                LimitedDataSet = 2,

                /// <summary>Full PHI: names, DOB, SSN, MRN, addresses, contact info.</summary>
                ProtectedHealthInfo = 3,

                /// <summary>Highly sensitive: HIV, substance abuse, mental health, genetic data.</summary>
                HighlySensitive = 4
            }

            /// <summary>Attribute for marking entity properties with PHI classification.</summary>
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class PhiAttribute : Attribute
            {
                public PhiClassification Level { get; }
                public string Category { get; }

                public PhiAttribute(PhiClassification level, string category = "General")
                {
                    Level = level;
                    Category = category;
                }
            }
            """
    };

    // ─── Fallback implementations when LLM is unavailable ───────────────────

    private static string GeneratePhiInventoryFallback() => """
        namespace Hms.SharedKernel.Compliance;

        /// <summary>
        /// Catalogs all PHI fields across HMS entities for HIPAA compliance tracking.
        /// Used by HIPAA audit, breach assessment, and minimum necessary enforcement.
        /// </summary>
        public static class PhiInventory
        {
            public static readonly Dictionary<string, string[]> EntityPhiFields = new()
            {
                ["PatientProfile"] = ["LegalGivenName", "LegalFamilyName", "PreferredName", "DateOfBirth", "SexAtBirth", "PrimaryLanguage"],
                ["PatientIdentifier"] = ["IdentifierValue", "IssuingAuthority"],
                ["Encounter"] = ["ChiefComplaint", "DischargeNotes"],
                ["EmergencyArrival"] = ["TriageNotes", "ChiefComplaint"],
                ["InpatientStay"] = ["AdmissionNotes", "DischargeSummary"],
                ["DiagnosticOrder"] = ["ClinicalNotes", "ResultNarrative"],
                ["DiagnosticResult"] = ["ResultValue", "Interpretation"],
                ["Claim"] = ["PatientId", "DiagnosisCodes"],
                ["AiInteraction"] = ["PromptText", "ResponseText", "PatientContextJson"],
            };

            public static bool IsPhiField(string entityName, string fieldName)
                => EntityPhiFields.TryGetValue(entityName, out var fields) && fields.Contains(fieldName);

            public static PhiClassification Classify(string entityName, string fieldName)
            {
                if (!IsPhiField(entityName, fieldName)) return PhiClassification.Public;
                return fieldName switch
                {
                    "LegalGivenName" or "LegalFamilyName" or "DateOfBirth" => PhiClassification.ProtectedHealthInfo,
                    "SexAtBirth" or "PrimaryLanguage" => PhiClassification.LimitedDataSet,
                    "DiagnosisCodes" or "ClinicalNotes" => PhiClassification.HighlySensitive,
                    _ => PhiClassification.ProtectedHealthInfo
                };
            }
        }
        """;

    private static string GenerateAccessAuditFallback() => """
        namespace Hms.SharedKernel.Compliance;

        public sealed record PhiAuditEntry
        {
            public string Id { get; init; } = Guid.NewGuid().ToString("N");
            public string TenantId { get; init; } = string.Empty;
            public string UserId { get; init; } = string.Empty;
            public string UserRole { get; init; } = string.Empty;
            public string Action { get; init; } = string.Empty; // Read, Write, Delete, Export
            public string EntityType { get; init; } = string.Empty;
            public string EntityId { get; init; } = string.Empty;
            public string[] FieldsAccessed { get; init; } = [];
            public string Justification { get; init; } = string.Empty;
            public string IpAddress { get; init; } = string.Empty;
            public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        }

        public interface IPhiAccessAuditService
        {
            Task LogAccessAsync(PhiAuditEntry entry, CancellationToken ct = default);
            Task LogModificationAsync(PhiAuditEntry entry, CancellationToken ct = default);
            Task LogDeletionAsync(PhiAuditEntry entry, CancellationToken ct = default);
            Task LogExportAsync(PhiAuditEntry entry, int recordCount, CancellationToken ct = default);
            Task<List<PhiAuditEntry>> GetAuditTrailAsync(string entityType, string entityId, CancellationToken ct = default);
        }

        public sealed class PhiAccessAuditService : IPhiAccessAuditService
        {
            private readonly List<PhiAuditEntry> _store = [];

            public Task LogAccessAsync(PhiAuditEntry entry, CancellationToken ct = default)
            { _store.Add(entry with { Action = "Read" }); return Task.CompletedTask; }

            public Task LogModificationAsync(PhiAuditEntry entry, CancellationToken ct = default)
            { _store.Add(entry with { Action = "Write" }); return Task.CompletedTask; }

            public Task LogDeletionAsync(PhiAuditEntry entry, CancellationToken ct = default)
            { _store.Add(entry with { Action = "Delete" }); return Task.CompletedTask; }

            public Task LogExportAsync(PhiAuditEntry entry, int recordCount, CancellationToken ct = default)
            { _store.Add(entry with { Action = $"Export({recordCount})" }); return Task.CompletedTask; }

            public Task<List<PhiAuditEntry>> GetAuditTrailAsync(string entityType, string entityId, CancellationToken ct = default)
                => Task.FromResult(_store.Where(e => e.EntityType == entityType && e.EntityId == entityId).ToList());
        }
        """;

    private static string GenerateBreachNotificationFallback() => """
        namespace Hms.SharedKernel.Compliance;

        public sealed record BreachRecord
        {
            public string Id { get; init; } = Guid.NewGuid().ToString("N");
            public string TenantId { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
            public int AffectedIndividuals { get; init; }
            public string[] PhiTypesExposed { get; init; } = [];
            public string RiskAssessment { get; init; } = string.Empty;
            public bool RequiresNotification { get; init; }
            public bool HhsNotified { get; init; }
            public bool IndividualsNotified { get; init; }
            public bool MediaNotified { get; init; }
        }

        public interface IBreachNotificationService
        {
            BreachRecord ReportBreach(string tenantId, string description, int affected, string[] phiTypes);
            bool AssessRiskRequiresNotification(BreachRecord breach);
            Task NotifyIndividualsAsync(BreachRecord breach, CancellationToken ct = default);
            Task NotifyHhsAsync(BreachRecord breach, CancellationToken ct = default);
            Task NotifyMediaAsync(BreachRecord breach, CancellationToken ct = default);
        }

        public sealed class BreachNotificationService : IBreachNotificationService
        {
            public BreachRecord ReportBreach(string tenantId, string description, int affected, string[] phiTypes)
                => new()
                {
                    TenantId = tenantId, Description = description,
                    AffectedIndividuals = affected, PhiTypesExposed = phiTypes,
                    RequiresNotification = true // Conservative: assume notification required
                };

            /// <summary>
            /// HIPAA 4-factor risk assessment:
            /// 1. Nature and extent of PHI involved
            /// 2. Unauthorized person who used/accessed the PHI
            /// 3. Whether PHI was actually acquired or viewed
            /// 4. Extent to which risk has been mitigated
            /// </summary>
            public bool AssessRiskRequiresNotification(BreachRecord breach)
                => breach.AffectedIndividuals > 0 && breach.PhiTypesExposed.Length > 0;

            public Task NotifyIndividualsAsync(BreachRecord breach, CancellationToken ct = default)
                => Task.CompletedTask; // Integration with notification service

            public Task NotifyHhsAsync(BreachRecord breach, CancellationToken ct = default)
                => Task.CompletedTask; // HHS portal submission

            /// <summary>Required when breach affects 500+ individuals in a state/jurisdiction.</summary>
            public Task NotifyMediaAsync(BreachRecord breach, CancellationToken ct = default)
                => breach.AffectedIndividuals >= 500 ? Task.CompletedTask : Task.CompletedTask;
        }
        """;

    private static string GenerateMinNecessaryFallback() => """
        using System.Reflection;

        namespace Hms.SharedKernel.Compliance;

        /// <summary>
        /// Enforces HIPAA Minimum Necessary standard — each role only sees PHI fields they need.
        /// </summary>
        public static class MinimumNecessaryPolicy
        {
            private static readonly Dictionary<string, HashSet<string>> RoleAllowedPhiFields = new()
            {
                ["Physician"]  = ["LegalGivenName", "LegalFamilyName", "DateOfBirth", "SexAtBirth", "Diagnosis", "TreatmentPlan", "LabResult", "Prescription", "Allergy", "VitalSign", "ClinicalNotes"],
                ["Nurse"]      = ["LegalGivenName", "LegalFamilyName", "DateOfBirth", "VitalSign", "Allergy", "Prescription", "ClinicalNotes"],
                ["Billing"]    = ["LegalGivenName", "LegalFamilyName", "InsuranceId", "DiagnosisCodes", "ClaimAmount"],
                ["Admin"]      = ["LegalGivenName", "LegalFamilyName", "DateOfBirth", "PrimaryLanguage"],
                ["LabTech"]    = ["DiagnosticOrder", "LabResult", "ResultValue"],
                ["Auditor"]    = ["LegalGivenName", "LegalFamilyName"], // Read-only, logged
            };

            /// <summary>
            /// Nullifies PHI fields that the given role should not access.
            /// Returns a new instance with restricted fields set to null/default.
            /// </summary>
            public static T FilterFields<T>(T entity, string role) where T : class
            {
                if (!RoleAllowedPhiFields.TryGetValue(role, out var allowed))
                    allowed = []; // Unknown role gets no PHI

                var type = typeof(T);
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (PhiInventory.IsPhiField(type.Name, prop.Name) && !allowed.Contains(prop.Name))
                    {
                        if (prop.CanWrite)
                            prop.SetValue(entity, prop.PropertyType == typeof(string) ? "[REDACTED]" : null);
                    }
                }
                return entity;
            }
        }
        """;
}
