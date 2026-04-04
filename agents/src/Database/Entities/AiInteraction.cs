using System.ComponentModel.DataAnnotations;

namespace Hms.Database.Entities.Ai;

public class AiInteraction
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    public string? FacilityId { get; set; }
    [Required] public string InteractionType { get; set; } = null!;
    public string? EncounterId { get; set; }
    public string? PatientId { get; set; }
    [Required] public string ModelVersion { get; set; } = null!;
    [Required] public string PromptVersion { get; set; } = null!;
    public string? InputSummaryJson { get; set; }
    public string? OutputSummaryJson { get; set; }
    [Required] public string OutcomeCode { get; set; } = null!;
    public string? AcceptedBy { get; set; }
    public string? RejectedBy { get; set; }
    public string? OverrideReason { get; set; }
    [Required] public string ClassificationCode { get; set; } = "ai_evidence";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;
}