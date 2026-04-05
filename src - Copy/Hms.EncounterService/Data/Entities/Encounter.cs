using System.ComponentModel.DataAnnotations;
namespace Hms.EncounterService.Data.Entities;

public class Encounter
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    [Required] public string FacilityId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string EncounterType { get; set; } = null!;
    public string? SourcePathway { get; set; }
    public string? AttendingProviderRef { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    [Required] public string StatusCode { get; set; } = "active";
    [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
    public bool LegalHoldFlag { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = null!;
    public int VersionNo { get; set; } = 1;
    public ICollection<ClinicalNote> Notes { get; set; } = [];
}