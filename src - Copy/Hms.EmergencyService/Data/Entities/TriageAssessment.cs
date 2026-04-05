using System.ComponentModel.DataAnnotations;
namespace Hms.EmergencyService.Data.Entities;

public class TriageAssessment
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string ArrivalId { get; set; } = null!;
    public string? PatientId { get; set; }
    [Required] public string AcuityLevel { get; set; } = null!;
    public string? ChiefComplaint { get; set; }
    public string VitalSnapshotJson { get; set; } = "{}";
    public bool ReTriageFlag { get; set; }
    public string? PathwayRecommendation { get; set; }
    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string PerformedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public EmergencyArrival Arrival { get; set; } = null!;
}