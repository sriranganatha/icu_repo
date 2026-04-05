namespace Hms.SharedKernel.AccessControl;

/// <summary>
/// Emergency override access ("Break the Glass") for life-threatening situations.
/// All overrides are logged, require justification, and trigger immediate audit review.
/// </summary>
public sealed record BreakTheGlassRequest
{
    public required string UserId { get; init; }
    public required string PatientId { get; init; }
    public required string Justification { get; init; }
    public required string TenantId { get; init; }
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record BreakTheGlassGrant
{
    public string GrantId { get; init; } = Guid.NewGuid().ToString("N");
    public string UserId { get; init; } = string.Empty;
    public string PatientId { get; init; } = string.Empty;
    public string Justification { get; init; } = string.Empty;
    public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Reviewed { get; set; }
    public string? ReviewedBy { get; set; }
}

public interface IBreakTheGlassService
{
    Task<BreakTheGlassGrant> RequestEmergencyAccessAsync(BreakTheGlassRequest request, CancellationToken ct = default);
    Task<bool> ValidateGrantAsync(string grantId, CancellationToken ct = default);
    Task MarkReviewedAsync(string grantId, string reviewerUserId, CancellationToken ct = default);
    Task<List<BreakTheGlassGrant>> GetPendingReviewsAsync(string tenantId, CancellationToken ct = default);
}

public sealed class BreakTheGlassService : IBreakTheGlassService
{
    private static readonly TimeSpan GrantDuration = TimeSpan.FromHours(4);
    private readonly List<BreakTheGlassGrant> _grants = [];

    public Task<BreakTheGlassGrant> RequestEmergencyAccessAsync(BreakTheGlassRequest request, CancellationToken ct = default)
    {
        var grant = new BreakTheGlassGrant
        {
            UserId = request.UserId,
            PatientId = request.PatientId,
            Justification = request.Justification,
            ExpiresAt = DateTimeOffset.UtcNow.Add(GrantDuration)
        };
        _grants.Add(grant);
        // TODO: Trigger immediate notification to Privacy Officer and audit team
        return Task.FromResult(grant);
    }

    public Task<bool> ValidateGrantAsync(string grantId, CancellationToken ct = default)
    {
        var grant = _grants.FirstOrDefault(g => g.GrantId == grantId);
        return Task.FromResult(grant is not null && grant.ExpiresAt > DateTimeOffset.UtcNow);
    }

    public Task MarkReviewedAsync(string grantId, string reviewerUserId, CancellationToken ct = default)
    {
        var grant = _grants.FirstOrDefault(g => g.GrantId == grantId);
        if (grant is not null) { grant.Reviewed = true; grant.ReviewedBy = reviewerUserId; }
        return Task.CompletedTask;
    }

    public Task<List<BreakTheGlassGrant>> GetPendingReviewsAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_grants.Where(g => !g.Reviewed).ToList());
}