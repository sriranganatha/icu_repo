namespace Hms.SharedKernel.AccessControl;

/// <summary>
/// Healthcare role definitions following least-privilege principle.
/// Each role has a defined scope of access to HMS resources.
/// </summary>
public static class HmsRoles
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