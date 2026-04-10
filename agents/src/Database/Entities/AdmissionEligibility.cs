using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Inpatient;

public class AdmissionEligibility
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string FacilityId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string EncounterId { get; set; } = null!;
    public string? CandidateClass { get; set; }
    [Required] public string DecisionCode { get; set; } = null!;
    public string? RationaleJson { get; set; }
    public string? PayerAuthorizationStatus { get; set; }
    public bool OverrideFlag { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
}