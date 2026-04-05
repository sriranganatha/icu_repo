using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hms.PatientService.Data;

namespace Hms.PatientService.Health;

public sealed class PatientServiceHealthCheck : IHealthCheck
{
    private readonly PatientServiceDbContext _db;
    public PatientServiceHealthCheck(PatientServiceDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect
                ? HealthCheckResult.Healthy("PatientService is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PatientService check failed", ex);
        }
    }
}