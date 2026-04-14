using GNex.Database;
using GNex.Database.Repositories;
using GNex.Database.Entities.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GNex.Tests.Integration;

/// <summary>
/// Shared test fixture providing an InMemory EF Core DbContext wired
/// with the real GNexDbContext configuration, tenant isolation, and
/// repository instances. Each test class gets an isolated DB instance.
/// </summary>
public class TestDbFixture : IDisposable
{
    public const string TestTenantId = "tenant-gnex-platform";
    public const string OtherTenantId = "tenant-acme-corp";

    private readonly string _dbName;

    public GNexDbContext Db { get; }

    public TestDbFixture()
    {
        _dbName = $"GNex_IntegrationTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<GNexDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;

        var tenantProvider = new TestTenantProvider(TestTenantId);
        Db = new GNexDbContext(options, tenantProvider);
        Db.Database.EnsureCreated();
    }

    public PlatformRepository<T> CreateRepo<T>() where T : PlatformEntityBase => new(Db);

    public GNexDbContext CreateDbForTenant(string tenantId)
    {
        // Shares the same InMemory database but with a different tenant
        var options = new DbContextOptionsBuilder<GNexDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;
        return new GNexDbContext(options, new TestTenantProvider(tenantId));
    }

    public ILogger<T> NullLogger<T>() => NullLoggerFactory.Instance.CreateLogger<T>();

    public void Dispose()
    {
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class TestTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public TestTenantProvider(string tenantId) => TenantId = tenantId;
}
