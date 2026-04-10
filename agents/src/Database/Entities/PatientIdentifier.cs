using System.ComponentModel.DataAnnotations;

namespace GNex.Database.Entities.Mpi;

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

    public PatientProfile Patient { get; set; } = null!;
}