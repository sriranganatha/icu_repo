using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.EncounterService.Data;

namespace Hms.EncounterService.Health;

public sealed class EncounterServiceHealthCheck : IHealthCheck
{
    private readonly EncounterServiceDbContext _db;
    public EncounterServiceHealthCheck(EncounterServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("EncounterService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("EncounterService check failed", ex);
        }
    }
}