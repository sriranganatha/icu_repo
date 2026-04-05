using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.AuditService.Data;

namespace Hms.AuditService.Health;

public sealed class AuditServiceHealthCheck : IHealthCheck
{
    private readonly AuditServiceDbContext _db;
    public AuditServiceHealthCheck(AuditServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("AuditService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("AuditService check failed", ex);
        }
    }
}