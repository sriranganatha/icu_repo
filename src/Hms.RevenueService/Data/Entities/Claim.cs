using System.ComponentModel.DataAnnotations;
namespace Hms.RevenueService.Data.Entities;

public class Claim
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    [Required] public string FacilityId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string EncounterRef { get; set; } = null!;
    [Required] public string PayerRef { get; set; } = null!;
    [Required] public string ClaimStatus { get; set; } = null!;
    public decimal BilledAmount { get; set; }
    public decimal? AllowedAmount { get; set; }
    [Required] public string ClassificationCode { get; set; } = "financial_sensitive";
    public bool LegalHoldFlag { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = null!;
    public int VersionNo { get; set; } = 1;
}