using System.ComponentModel.DataAnnotations;
namespace Hms.PatientService.Data.Entities;

public class PatientIdentifier
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required] public string TenantId { get; set; } = null!;
    [Required] public string PatientId { get; set; } = null!;
    [Required] public string IdentifierType { get; set; } = null!;
    [Required] public string IdentifierValueHash { get; set; } = null!;
    public string? Issuer { get; set; }
    [Required] public string StatusCode { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required] public string UpdatedBy { get; set; } = null!;
    public PatientProfile Patient { get; set; } = null!;
}