using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Emergency;

public class EmergencyArrival
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string RegionId { get; set; } = null!;
    [Required] public string FacilityId { get; set; } = null!;
    public string? PatientId { get; set; }
    public string? TemporaryIdentityAlias { get; set; }
    public string? ArrivalMode { get; set; }
    public string? ChiefComplaint { get; set; }
    public string? HandoffSource { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string CreatedBy { get; set; } = null!;

    public ICollection<TriageAssessment> Triages { get; set; } = [];
}