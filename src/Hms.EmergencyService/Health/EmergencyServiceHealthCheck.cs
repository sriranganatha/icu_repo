using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.EmergencyService.Data;

namespace Hms.EmergencyService.Health;

public sealed class EmergencyServiceHealthCheck : IHealthCheck
{
    private readonly EmergencyServiceDbContext _db;
    public EmergencyServiceHealthCheck(EmergencyServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("EmergencyService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("EmergencyService check failed", ex);
        }
    }
}