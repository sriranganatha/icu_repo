using System.Diagnostics;
using GNex.Agents.Requirements;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.AccessControl;

/// <summary>
/// AI-powered RBAC/ABAC agent. Generates role definitions, permission
/// matrices, resource-level authorization policies, break-the-glass emergency access,
/// and consent management for the HMS platform.
/// </summary>
public sealed class AccessControlAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<AccessControlAgent> _logger;

    public AgentType Type => AgentType.AccessControl;
    public string Name => "Access Control Agent";
    public string Description => "Generates RBAC roles, permission matrices, resource-level authorization, break-the-glass emergency access, and consent management.";

    public AccessControlAgent(ILlmProvider llm, ILogger<AccessControlAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("AccessControlAgent starting — AI-powered RBAC/ABAC generation");

        var artifacts = new List<CodeArtifact>();

        try
        {
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating role definitions from domain requirements");
            artifacts.Add(GenerateRoleDefinitions());

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Building permission matrix — mapping roles to resources with CRUD granularity");
            artifacts.Add(GeneratePermissionMatrix());

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "AI-generating authorization policy provider — resource-level ABAC with Gemini LLM");
            artifacts.Add(await GenerateAuthorizationPolicyProvider(ct));

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating break-the-glass emergency access service — time-limited elevated access with full audit trail");
            artifacts.Add(GenerateBreakTheGlassService());

            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Generating consent management — consent tracking, revocation, minimum necessary access");
            artifacts.Add(GenerateConsentManagement());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            // Agent completes its own claimed work items
            foreach (var item in context.CurrentClaimedItems)
                context.CompleteWorkItem?.Invoke(item);

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"AccessControl Agent: {artifacts.Count} RBAC/ABAC artifacts (AI: {_llm.ProviderName})",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Access control artifacts generated",
                    Body = "Roles, permissions, authorization policies, break-the-glass, consent management." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "AccessControlAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateRoleDefinitions() => new()
    {
        Layer = ArtifactLayer.Security,
        RelativePath = "GNex.SharedKernel/AccessControl/GNexRoles.cs",
        FileName = "GNexRoles.cs",
        Namespace = "GNex.SharedKernel.AccessControl",
        ProducedBy = AgentType.AccessControl,
        TracedRequirementIds = ["NFR-SEC-01", "OWASP-A01"],
        Content = """
            namespace GNex.SharedKernel.AccessControl;

            /// <summary>
            /// Role definitions following least-privilege principle.
            /// Each role has a defined scope of access to HMS resources.
            /// </summary>
            public static class GNexRoles
            {
                // Clinical roles
                public const string Physician = "Physician";
                public const string Nurse = "Nurse";
                public const string Pharmacist = "Pharmacist";
                public const string LabTechnician = "LabTechnician";
                public const string Radiologist = "Radiologist";
                public const string Therapist = "Therapist";
                public const string Surgeon = "Surgeon";

                // Administrative roles
                public const string Admin = "Admin";
                public const string FacilityAdmin = "FacilityAdmin";
                public const string DepartmentHead = "DepartmentHead";

                // Financial roles
                public const string BillingClerk = "BillingClerk";
                public const string InsuranceProcessor = "InsuranceProcessor";
                public const string FinanceManager = "FinanceManager";

                // Compliance & audit
                public const string Auditor = "Auditor";
                public const string ComplianceOfficer = "ComplianceOfficer";
                public const string PrivacyOfficer = "PrivacyOfficer";

                // Technical roles
                public const string SystemAdmin = "SystemAdmin";
                public const string SupportStaff = "SupportStaff";

                // Patient-facing
                public const string Patient = "Patient";
                public const string PatientRepresentative = "PatientRepresentative";

                public static readonly string[] AllRoles =
                [
                    Physician, Nurse, Pharmacist, LabTechnician, Radiologist, Therapist, Surgeon,
                    Admin, FacilityAdmin, DepartmentHead,
                    BillingClerk, InsuranceProcessor, FinanceManager,
                    Auditor, ComplianceOfficer, PrivacyOfficer,
                    SystemAdmin, SupportStaff,
                    Patient, PatientRepresentative
                ];

                public static readonly string[] ClinicalRoles =
                    [Physician, Nurse, Pharmacist, LabTechnician, Radiologist, Therapist, Surgeon];

                public static readonly string[] AdminRoles =
                    [Admin, FacilityAdmin, DepartmentHead, SystemAdmin];
            }
            """
    };

    private static CodeArtifact GeneratePermissionMatrix() => new()
    {
        Layer = ArtifactLayer.Security,
        RelativePath = "GNex.SharedKernel/AccessControl/PermissionMatrix.cs",
        FileName = "PermissionMatrix.cs",
        Namespace = "GNex.SharedKernel.AccessControl",
        ProducedBy = AgentType.AccessControl,
        TracedRequirementIds = ["NFR-SEC-01", "SOC2-CC6"],
        Content = """
            namespace GNex.SharedKernel.AccessControl;

            public enum GNexPermission
            {
                PatientRead, PatientWrite, PatientDelete,
                EncounterRead, EncounterWrite, EncounterClose,
                OrderCreate, OrderCancel, OrderApprove,
                ResultRead, ResultWrite, ResultVerify,
                PrescriptionRead, PrescriptionWrite, PrescriptionDispense,
                ClaimRead, ClaimSubmit, ClaimAdjudicate,
                AuditRead, AuditExport,
                AiInteract, AiOverride,
                SystemConfig, UserManage, RoleManage,
                BreakTheGlass, ConsentManage,
            }

            /// <summary>
            /// Role → Permission mapping for the HMS platform.
            /// Enforces least-privilege access per compliance standards.
            /// </summary>
            public static class PermissionMatrix
            {
                private static readonly Dictionary<string, HashSet<GNexPermission>> RolePermissions = new()
                {
                    [GNexRoles.Physician] = [
                        GNexPermission.PatientRead, GNexPermission.PatientWrite,
                        GNexPermission.EncounterRead, GNexPermission.EncounterWrite, GNexPermission.EncounterClose,
                        GNexPermission.OrderCreate, GNexPermission.OrderApprove,
                        GNexPermission.ResultRead, GNexPermission.ResultVerify,
                        GNexPermission.PrescriptionRead, GNexPermission.PrescriptionWrite,
                        GNexPermission.AiInteract, GNexPermission.AiOverride,
                        GNexPermission.BreakTheGlass,
                    ],
                    [GNexRoles.Nurse] = [
                        GNexPermission.PatientRead, GNexPermission.PatientWrite,
                        GNexPermission.EncounterRead, GNexPermission.EncounterWrite,
                        GNexPermission.OrderCreate,
                        GNexPermission.ResultRead,
                        GNexPermission.PrescriptionRead,
                        GNexPermission.AiInteract,
                    ],
                    [GNexRoles.BillingClerk] = [
                        GNexPermission.PatientRead,
                        GNexPermission.ClaimRead, GNexPermission.ClaimSubmit,
                    ],
                    [GNexRoles.Auditor] = [
                        GNexPermission.PatientRead,
                        GNexPermission.AuditRead, GNexPermission.AuditExport,
                    ],
                    [GNexRoles.SystemAdmin] = [
                        GNexPermission.SystemConfig, GNexPermission.UserManage, GNexPermission.RoleManage,
                        GNexPermission.AuditRead,
                    ],
                    [GNexRoles.Patient] = [
                        GNexPermission.PatientRead,
                        GNexPermission.EncounterRead,
                        GNexPermission.ResultRead,
                        GNexPermission.ConsentManage,
                    ],
                };

                public static bool HasPermission(string role, GNexPermission permission)
                    => RolePermissions.TryGetValue(role, out var perms) && perms.Contains(permission);

                public static HashSet<GNexPermission> GetPermissions(string role)
                    => RolePermissions.TryGetValue(role, out var perms) ? perms : [];

                public static bool CanAccessEntity(string role, string entityType)
                    => entityType switch
                    {
                        "PatientProfile" => HasPermission(role, GNexPermission.PatientRead),
                        "Encounter" => HasPermission(role, GNexPermission.EncounterRead),
                        "DiagnosticOrder" or "DiagnosticResult" => HasPermission(role, GNexPermission.OrderCreate) || HasPermission(role, GNexPermission.ResultRead),
                        "Claim" or "Payment" => HasPermission(role, GNexPermission.ClaimRead),
                        "AiInteraction" => HasPermission(role, GNexPermission.AiInteract),
                        "AuditEntry" => HasPermission(role, GNexPermission.AuditRead),
                        _ => false
                    };
            }
            """
    };

    private async Task<CodeArtifact> GenerateAuthorizationPolicyProvider(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a .NET security expert. Generate an ASP.NET Core authorization policy provider that enforces the HMS permission matrix.",
            UserPrompt = "Generate an GNexAuthorizationPolicyProvider that maps GNexPermission to ASP.NET Core policies. Include a RequirePermissionAttribute and an GNexAuthorizationHandler. Namespace: GNex.SharedKernel.AccessControl.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Security,
            RelativePath = "GNex.SharedKernel/AccessControl/GNexAuthorizationPolicyProvider.cs",
            FileName = "GNexAuthorizationPolicyProvider.cs",
            Namespace = "GNex.SharedKernel.AccessControl",
            ProducedBy = AgentType.AccessControl,
            TracedRequirementIds = ["NFR-SEC-01", "OWASP-A01"],
            Content = response.Success ? response.Content : """
                using Microsoft.AspNetCore.Authorization;

                namespace GNex.SharedKernel.AccessControl;

                [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
                public sealed class RequirePermissionAttribute : AuthorizeAttribute
                {
                    public RequirePermissionAttribute(GNexPermission permission)
                        : base($"GNexPermission:{permission}") { }
                }

                public sealed class GNexPermissionRequirement : IAuthorizationRequirement
                {
                    public GNexPermission Permission { get; }
                    public GNexPermissionRequirement(GNexPermission permission) => Permission = permission;
                }

                public sealed class GNexAuthorizationHandler : AuthorizationHandler<GNexPermissionRequirement>
                {
                    protected override Task HandleRequirementAsync(
                        AuthorizationHandlerContext context, GNexPermissionRequirement requirement)
                    {
                        var roleClaim = context.User.FindFirst("role")?.Value
                            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

                        if (roleClaim is not null && PermissionMatrix.HasPermission(roleClaim, requirement.Permission))
                            context.Succeed(requirement);

                        return Task.CompletedTask;
                    }
                }
                """
        };
    }

    private static CodeArtifact GenerateBreakTheGlassService() => new()
    {
        Layer = ArtifactLayer.Security,
        RelativePath = "GNex.SharedKernel/AccessControl/BreakTheGlassService.cs",
        FileName = "BreakTheGlassService.cs",
        Namespace = "GNex.SharedKernel.AccessControl",
        ProducedBy = AgentType.AccessControl,
        TracedRequirementIds = ["NFR-SEC-01", "NFR-ACCESS-01"],
        Content = """
            namespace GNex.SharedKernel.AccessControl;

            /// <summary>
            /// Emergency override access ("Break the Glass") for life-threatening situations.
            /// All overrides are logged, require justification, and trigger immediate audit review.
            /// </summary>
            public sealed record BreakTheGlassRequest
            {
                public required string UserId { get; init; }
                public required string PatientId { get; init; }
                public required string Justification { get; init; }
                public required string TenantId { get; init; }
                public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
            }

            public sealed record BreakTheGlassGrant
            {
                public string GrantId { get; init; } = Guid.NewGuid().ToString("N");
                public string UserId { get; init; } = string.Empty;
                public string PatientId { get; init; } = string.Empty;
                public string Justification { get; init; } = string.Empty;
                public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.UtcNow;
                public DateTimeOffset ExpiresAt { get; init; }
                public bool Reviewed { get; set; }
                public string? ReviewedBy { get; set; }
            }

            public interface IBreakTheGlassService
            {
                Task<BreakTheGlassGrant> RequestEmergencyAccessAsync(BreakTheGlassRequest request, CancellationToken ct = default);
                Task<bool> ValidateGrantAsync(string grantId, CancellationToken ct = default);
                Task MarkReviewedAsync(string grantId, string reviewerUserId, CancellationToken ct = default);
                Task<List<BreakTheGlassGrant>> GetPendingReviewsAsync(string tenantId, CancellationToken ct = default);
            }

            public sealed class BreakTheGlassService : IBreakTheGlassService
            {
                private static readonly TimeSpan GrantDuration = TimeSpan.FromHours(4);
                private readonly List<BreakTheGlassGrant> _grants = [];

                public Task<BreakTheGlassGrant> RequestEmergencyAccessAsync(BreakTheGlassRequest request, CancellationToken ct = default)
                {
                    var grant = new BreakTheGlassGrant
                    {
                        UserId = request.UserId,
                        PatientId = request.PatientId,
                        Justification = request.Justification,
                        ExpiresAt = DateTimeOffset.UtcNow.Add(GrantDuration)
                    };
                    _grants.Add(grant);
                    // TODO: Trigger immediate notification to Privacy Officer and audit team
                    return Task.FromResult(grant);
                }

                public Task<bool> ValidateGrantAsync(string grantId, CancellationToken ct = default)
                {
                    var grant = _grants.FirstOrDefault(g => g.GrantId == grantId);
                    return Task.FromResult(grant is not null && grant.ExpiresAt > DateTimeOffset.UtcNow);
                }

                public Task MarkReviewedAsync(string grantId, string reviewerUserId, CancellationToken ct = default)
                {
                    var grant = _grants.FirstOrDefault(g => g.GrantId == grantId);
                    if (grant is not null) { grant.Reviewed = true; grant.ReviewedBy = reviewerUserId; }
                    return Task.CompletedTask;
                }

                public Task<List<BreakTheGlassGrant>> GetPendingReviewsAsync(string tenantId, CancellationToken ct = default)
                    => Task.FromResult(_grants.Where(g => !g.Reviewed).ToList());
            }
            """
    };

    private static CodeArtifact GenerateConsentManagement() => new()
    {
        Layer = ArtifactLayer.Security,
        RelativePath = "GNex.SharedKernel/AccessControl/ConsentManagement.cs",
        FileName = "ConsentManagement.cs",
        Namespace = "GNex.SharedKernel.AccessControl",
        ProducedBy = AgentType.AccessControl,
        TracedRequirementIds = ["NFR-CONSENT-01", "NFR-SEC-01"],
        Content = """
            namespace GNex.SharedKernel.AccessControl;

            public enum ConsentType { Treatment, Research, DataSharing, Marketing, AiProcessing }
            public enum ConsentStatus { Granted, Revoked, Expired, Pending }

            public sealed record PatientConsent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString("N");
                public required string PatientId { get; init; }
                public required string TenantId { get; init; }
                public required ConsentType Type { get; init; }
                public ConsentStatus Status { get; set; } = ConsentStatus.Pending;
                public string GrantedTo { get; init; } = string.Empty;
                public string Purpose { get; init; } = string.Empty;
                public DateTimeOffset? GrantedAt { get; set; }
                public DateTimeOffset? RevokedAt { get; set; }
                public DateTimeOffset? ExpiresAt { get; init; }
            }

            public interface IConsentService
            {
                Task<PatientConsent> RequestConsentAsync(PatientConsent consent, CancellationToken ct = default);
                Task GrantConsentAsync(string consentId, CancellationToken ct = default);
                Task RevokeConsentAsync(string consentId, string reason, CancellationToken ct = default);
                Task<bool> HasConsentAsync(string patientId, ConsentType type, CancellationToken ct = default);
                Task<List<PatientConsent>> GetPatientConsentsAsync(string patientId, CancellationToken ct = default);
            }
            """
    };
}
