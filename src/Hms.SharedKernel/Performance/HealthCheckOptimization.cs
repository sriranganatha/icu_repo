using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hms.SharedKernel.Performance;

/// <summary>
/// Lightweight health check that avoids hitting the database on every probe.
/// Uses a cached status with a configurable TTL to reduce DB load from
/// Kubernetes liveness/readiness probes (typically every 10s per pod).
/// </summary>
public sealed class CachedDbHealthCheck : IHealthCheck
{
    private readonly Func<CancellationToken, Task<bool>> _dbCheck;
    private readonly TimeSpan _cacheTtl;
    private volatile HealthCheckResult _cached = HealthCheckResult.Healthy("Not yet checked");
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

    public CachedDbHealthCheck(Func<CancellationToken, Task<bool>> dbCheck, TimeSpan? cacheTtl = null)
    {
        _dbCheck = dbCheck;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(15);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _lastCheck < _cacheTtl)
            return _cached;

        try
        {
            var ok = await _dbCheck(ct);
            _cached = ok
                ? HealthCheckResult.Healthy("DB reachable")
                : HealthCheckResult.Degraded("DB unreachable");
        }
        catch (Exception ex)
        {
            _cached = HealthCheckResult.Unhealthy("DB check failed", ex);
        }

        _lastCheck = DateTimeOffset.UtcNow;
        return _cached;
    }
}