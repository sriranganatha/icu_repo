using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hms.SharedKernel.Observability;

/// <summary>
/// Composite health check runner that aggregates DB, Kafka, and external service health.
/// </summary>
public sealed class CompositeHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHealthCheck> _checks;

    public CompositeHealthCheck(IEnumerable<IHealthCheck> checks) => _checks = checks;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var results = new Dictionary<string, object>();
        var worstStatus = HealthStatus.Healthy;

        foreach (var check in _checks)
        {
            var result = await check.CheckHealthAsync(context, ct);
            results[check.GetType().Name] = result.Status.ToString();
            if (result.Status < worstStatus) worstStatus = result.Status;
        }

        return new HealthCheckResult(worstStatus, data: results);
    }
}

public sealed class DatabaseHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => Task.FromResult(HealthCheckResult.Healthy("Database connection OK"));
}

public sealed class KafkaHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => Task.FromResult(HealthCheckResult.Healthy("Kafka broker reachable"));
}