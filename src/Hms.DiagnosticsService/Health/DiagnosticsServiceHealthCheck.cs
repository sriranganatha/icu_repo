using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.DiagnosticsService.Data;

namespace Hms.DiagnosticsService.Health;

public sealed class DiagnosticsServiceHealthCheck : IHealthCheck
{
    private readonly DiagnosticsServiceDbContext _db;
    public DiagnosticsServiceHealthCheck(DiagnosticsServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("DiagnosticsService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("DiagnosticsService check failed", ex);
        }
    }
}