using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.InpatientService.Data;

namespace Hms.InpatientService.Health;

public sealed class InpatientServiceHealthCheck : IHealthCheck
{
    private readonly InpatientServiceDbContext _db;
    public InpatientServiceHealthCheck(InpatientServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("InpatientService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("InpatientService check failed", ex);
        }
    }
}