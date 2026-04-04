using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.AiService.Data;

namespace Hms.AiService.Health;

public sealed class AiServiceHealthCheck : IHealthCheck
{
    private readonly AiServiceDbContext _db;
    public AiServiceHealthCheck(AiServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("AiService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("AiService check failed", ex);
        }
    }
}