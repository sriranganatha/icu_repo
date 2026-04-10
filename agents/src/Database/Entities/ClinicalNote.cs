using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Clinical;

public class ClinicalNote
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string EncounterId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string NoteType { get; set; } = null!;
    public string? NoteClassificationCode { get; set; }
    [Required] public string ContentJson { get; set; } = "{}";
    public string? AiInteractionId { get; set; }
    public DateTimeOffset AuthoredAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string AuthoredBy { get; set; } = null!;
    public string? AmendedFromNoteId { get; set; }
    public int VersionNo { get; set; } = 1;
    public bool LegalHoldFlag { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Encounter Encounter { get; set; } = null!;
}