namespace Hms.SharedKernel.AccessControl;

public enum HmsPermission
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
/// Enforces least-privilege access per healthcare compliance requirements.
/// </summary>
public static class PermissionMatrix
{
    private static readonly Dictionary<string, HashSet<HmsPermission>> RolePermissions = new()
    {
        [HmsRoles.Physician] = [
            HmsPermission.PatientRead, HmsPermission.PatientWrite,
            HmsPermission.EncounterRead, HmsPermission.EncounterWrite, HmsPermission.EncounterClose,
            HmsPermission.OrderCreate, HmsPermission.OrderApprove,
            HmsPermission.ResultRead, HmsPermission.ResultVerify,
            HmsPermission.PrescriptionRead, HmsPermission.PrescriptionWrite,
            HmsPermission.AiInteract, HmsPermission.AiOverride,
            HmsPermission.BreakTheGlass,
        ],
        [HmsRoles.Nurse] = [
            HmsPermission.PatientRead, HmsPermission.PatientWrite,
            HmsPermission.EncounterRead, HmsPermission.EncounterWrite,
            HmsPermission.OrderCreate,
            HmsPermission.ResultRead,
            HmsPermission.PrescriptionRead,
            HmsPermission.AiInteract,
        ],
        [HmsRoles.BillingClerk] = [
            HmsPermission.PatientRead,
            HmsPermission.ClaimRead, HmsPermission.ClaimSubmit,
        ],
        [HmsRoles.Auditor] = [
            HmsPermission.PatientRead,
            HmsPermission.AuditRead, HmsPermission.AuditExport,
        ],
        [HmsRoles.SystemAdmin] = [
            HmsPermission.SystemConfig, HmsPermission.UserManage, HmsPermission.RoleManage,
            HmsPermission.AuditRead,
        ],
        [HmsRoles.Patient] = [
            HmsPermission.PatientRead,
            HmsPermission.EncounterRead,
            HmsPermission.ResultRead,
            HmsPermission.ConsentManage,
        ],
    };

    public static bool HasPermission(string role, HmsPermission permission)
        => RolePermissions.TryGetValue(role, out var perms) && perms.Contains(permission);

    public static HashSet<HmsPermission> GetPermissions(string role)
        => RolePermissions.TryGetValue(role, out var perms) ? perms : [];

    public static bool CanAccessEntity(string role, string entityType)
        => entityType switch
        {
            "PatientProfile" => HasPermission(role, HmsPermission.PatientRead),
            "Encounter" => HasPermission(role, HmsPermission.EncounterRead),
            "DiagnosticOrder" or "DiagnosticResult" => HasPermission(role, HmsPermission.OrderCreate) || HasPermission(role, HmsPermission.ResultRead),
            "Claim" or "Payment" => HasPermission(role, HmsPermission.ClaimRead),
            "AiInteraction" => HasPermission(role, HmsPermission.AiInteract),
            "AuditEntry" => HasPermission(role, HmsPermission.AuditRead),
            _ => false
        };
}