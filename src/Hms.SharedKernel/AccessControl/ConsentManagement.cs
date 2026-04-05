namespace Hms.SharedKernel.AccessControl;

public enum ConsentType { Treatment, Research, DataSharing, Marketing, AiProcessing }
public enum ConsentStatus { Granted, Revoked, Expired, Pending }

public sealed record PatientConsent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string PatientId { get; init; }
    public required string TenantId { get; init; }
    public required ConsentType Type { get; init; }
    public ConsentStatus Status { get; set; } = ConsentStatus.Pending;
    public string GrantedTo { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public DateTimeOffset? GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public interface IConsentService
{
    Task<PatientConsent> RequestConsentAsync(PatientConsent consent, CancellationToken ct = default);
    Task GrantConsentAsync(string consentId, CancellationToken ct = default);
    Task RevokeConsentAsync(string consentId, string reason, CancellationToken ct = default);
    Task<bool> HasConsentAsync(string patientId, ConsentType type, CancellationToken ct = default);
    Task<List<PatientConsent>> GetPatientConsentsAsync(string patientId, CancellationToken ct = default);
}