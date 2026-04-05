using Microsoft.Extensions.Logging;

namespace Hms.SharedKernel.Infrastructure;

/// <summary>
/// EF Core migration runner for startup. Each service calls RunAsync()
/// in Program.cs to auto-apply pending migrations.
/// </summary>
public sealed class DatabaseMigrationRunner
{
    private readonly ILogger<DatabaseMigrationRunner> _logger;

    public DatabaseMigrationRunner(ILogger<DatabaseMigrationRunner> logger) => _logger = logger;

    /// <summary>
    /// Apply pending EF Core migrations at startup with retry logic.
    /// </summary>
    public async Task RunAsync(object dbContext, CancellationToken ct = default)
    {
        const int maxRetries = 5;
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Applying database migrations (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);

                // Uses reflection to call dbContext.Database.MigrateAsync()
                var dbProp = dbContext.GetType().GetProperty("Database")
                    ?? throw new InvalidOperationException("DbContext must have a Database property");
                var database = dbProp.GetValue(dbContext)!;
                var migrateMethod = database.GetType().GetMethod("MigrateAsync",
                    [typeof(CancellationToken)]);

                if (migrateMethod is not null)
                {
                    var task = (Task?)migrateMethod.Invoke(database, [ct]);
                    if (task is not null) await task;
                }

                _logger.LogInformation("Database migrations applied successfully");
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Migration attempt {Attempt} failed, retrying in {Delay}s", attempt, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay *= 2; // Exponential backoff
            }
        }
    }
}