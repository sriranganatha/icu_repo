using System.ComponentModel.DataAnnotations;
namespace Hms.InpatientService.Data.Entities;

public class Admission
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    [Required] public string FacilityId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string EncounterId { get; set; } = null!;
    [Required] public string AdmitClass { get; set; } = null!;
    public string? AdmitSource { get; set; }
    [Required] public string StatusCode { get; set; } = "active";
    public DateTimeOffset? ExpectedDischargeAt { get; set; }
    public string? UtilizationStatusCode { get; set; }
    [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
    public bool LegalHoldFlag { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = null!;
    public int VersionNo { get; set; } = 1;
}