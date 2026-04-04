using System.ComponentModel.DataAnnotations;
namespace Hms.PatientService.Data.Entities;

public class PatientProfile
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    public string? FacilityId { get; set; }
    [Required] public string EnterprisePersonKey { get; set; } = null!;
    [Required] public string LegalGivenName { get; set; } = null!;
    [Required] public string LegalFamilyName { get; set; } = null!;
    public string? PreferredName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string? SexAtBirth { get; set; }
    public string? PrimaryLanguage { get; set; }
    [Required] public string StatusCode { get; set; } = "active";
    [Required] public string ClassificationCode { get; set; } = "clinical_restricted";
    public bool LegalHoldFlag { get; set; }
    public string? SourceSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = null!;
    public int VersionNo { get; set; } = 1;
    public ICollection<PatientIdentifier> Identifiers { get; set; } = [];
}