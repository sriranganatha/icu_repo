using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.RevenueService.Data;

namespace Hms.RevenueService.Health;

public sealed class RevenueServiceHealthCheck : IHealthCheck
{
    private readonly RevenueServiceDbContext _db;
    public RevenueServiceHealthCheck(RevenueServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("RevenueService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RevenueService check failed", ex);
        }
    }
}