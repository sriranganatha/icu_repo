using FluentAssertions;
using Hms.Database;
using Hms.Database.Entities.Platform;
using Hms.Database.Entities.Platform.Technology;
using Hms.Database.Entities.Platform.Projects;
using Hms.Database.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HmsAgents.Tests;

/// <summary>
/// Repository tests using in-memory EF Core provider.
/// Tests PlatformRepository CRUD, soft delete, pagination, and tenant scoping.
/// </summary>
public class RepositoryTests : IDisposable
{
    private readonly HmsDbContext _db;
    private readonly PlatformRepository<Language> _repo;

    public RepositoryTests()
    {
        var options = new DbContextOptionsBuilder<HmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var tenantProvider = new TestTenantProvider("test-tenant");
        _db = new HmsDbContext(options, tenantProvider);
        _repo = new PlatformRepository<Language>(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedAt()
    {
        var lang = new Language { Name = "C#", Version = "12", TenantId = "test-tenant" };

        var result = await _repo.CreateAsync(lang);

        result.Id.Should().NotBeNullOrEmpty();
        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntity()
    {
        var lang = new Language { Name = "Python", Version = "3.12", TenantId = "test-tenant" };
        await _repo.CreateAsync(lang);

        var result = await _repo.GetByIdAsync(lang.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Python");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsPaginated()
    {
        for (int i = 0; i < 10; i++)
            await _repo.CreateAsync(new Language { Name = $"Lang{i}", Version = "1.0", TenantId = "test-tenant" });

        var page1 = await _repo.ListAsync(skip: 0, take: 3);
        var page2 = await _repo.ListAsync(skip: 3, take: 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(3);
        page1.Select(l => l.Id).Should().NotIntersectWith(page2.Select(l => l.Id));
    }

    [Fact]
    public async Task ListAsync_ExcludesInactive()
    {
        var active = new Language { Name = "Active", Version = "1", TenantId = "test-tenant" };
        var inactive = new Language { Name = "Inactive", Version = "1", TenantId = "test-tenant", IsActive = false };
        await _repo.CreateAsync(active);
        await _repo.CreateAsync(inactive);

        var result = await _repo.ListAsync();

        result.Should().ContainSingle(l => l.Name == "Active");
        result.Should().NotContain(l => l.Name == "Inactive");
    }

    [Fact]
    public async Task QueryAsync_FiltersByPredicate()
    {
        await _repo.CreateAsync(new Language { Name = "C#", Version = "12", TenantId = "test-tenant" });
        await _repo.CreateAsync(new Language { Name = "Java", Version = "21", TenantId = "test-tenant" });

        var result = await _repo.QueryAsync(l => l.Name == "Java");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Java");
    }

    [Fact]
    public async Task CountAsync_CountsActive()
    {
        await _repo.CreateAsync(new Language { Name = "Go", Version = "1.22", TenantId = "test-tenant" });
        await _repo.CreateAsync(new Language { Name = "Rust", Version = "1.78", TenantId = "test-tenant" });

        var count = await _repo.CountAsync();

        count.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_WithPredicate()
    {
        await _repo.CreateAsync(new Language { Name = "Go", Version = "1.22", TenantId = "test-tenant" });
        await _repo.CreateAsync(new Language { Name = "Rust", Version = "1.78", TenantId = "test-tenant" });

        var count = await _repo.CountAsync(l => l.Name == "Go");

        count.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_IncrementsVersion()
    {
        var lang = new Language { Name = "TypeScript", Version = "5.4", TenantId = "test-tenant" };
        await _repo.CreateAsync(lang);

        lang.Version = "5.5";
        await _repo.UpdateAsync(lang);

        var updated = await _repo.GetByIdAsync(lang.Id);
        updated!.Version.Should().Be("5.5");
        updated.VersionNo.Should().Be(2);
    }

    [Fact]
    public async Task SoftDeleteAsync_SetsInactiveAndArchived()
    {
        var lang = new Language { Name = "Kotlin", Version = "2.0", TenantId = "test-tenant" };
        await _repo.CreateAsync(lang);

        await _repo.SoftDeleteAsync(lang.Id);

        var deleted = await _repo.GetByIdAsync(lang.Id);
        deleted!.IsActive.Should().BeFalse();
        deleted.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SoftDeleteAsync_ExcludesFromList()
    {
        var lang = new Language { Name = "Swift", Version = "5.10", TenantId = "test-tenant" };
        await _repo.CreateAsync(lang);
        await _repo.SoftDeleteAsync(lang.Id);

        var list = await _repo.ListAsync();

        list.Should().NotContain(l => l.Name == "Swift");
    }

    [Fact]
    public async Task RestoreAsync_SetsActiveAgain()
    {
        var lang = new Language { Name = "Dart", Version = "3.3", TenantId = "test-tenant" };
        await _repo.CreateAsync(lang);
        await _repo.SoftDeleteAsync(lang.Id);
        await _repo.RestoreAsync(lang.Id);

        var restored = await _repo.GetByIdAsync(lang.Id);
        restored!.IsActive.Should().BeTrue();
        restored.ArchivedAt.Should().BeNull();
    }

    // ── Project repository tests ──

    [Fact]
    public async Task ProjectRepository_CRUD()
    {
        var projectRepo = new PlatformRepository<Project>(_db);
        var project = new Project
        {
            Name = "Test HMS",
            Slug = "test-hms",
            Description = "Test project",
            TenantId = "test-tenant"
        };

        var created = await projectRepo.CreateAsync(project);
        created.Status.Should().Be("draft");

        var fetched = await projectRepo.GetByIdAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Test HMS");
    }

    private sealed class TestTenantProvider : ITenantProvider
    {
        public string TenantId { get; }
        public TestTenantProvider(string tenantId) => TenantId = tenantId;
    }
}
