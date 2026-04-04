using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Diagnostics;

public class ResultRecord
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    [Required] public string FacilityId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string OrderId { get; set; } = null!;
    [Required] public string AnalyteCode { get; set; } = null!;
    public string? MeasuredValue { get; set; }
    public string? UnitCode { get; set; }
    public string? AbnormalFlag { get; set; }
    public bool CriticalFlag { get; set; }
    public DateTimeOffset ResultAt { get; set; }
    [Required] public string RecordedBy { get; set; } = null!;
    [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
    public bool LegalHoldFlag { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = null!;
    public int VersionNo { get; set; } = 1;
}