using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.AuditService.Data;
using Hms.AuditService.Data.Entities;
using Hms.AuditService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for AuditEvent using EF Core InMemory provider.
/// Feature coverage: EP-Y1, Module-Y, Compliance
/// </summary>
public class AuditEventRepositoryTests : IDisposable
{
    private readonly AuditServiceDbContext _db;
    private readonly AuditEventRepository _repo;

    public AuditEventRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AuditServiceDbContext>()
            .UseInMemoryDatabase($"AuditEvent_2be8addfa4924b988b846e55c8e0202c")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new AuditServiceDbContext(options, tenant);
        _repo = new AuditEventRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new AuditEvent
        {

        };

        var saved = await _repo.CreateAsync(entity);

        saved.Id.Should().NotBeNullOrEmpty();
        var loaded = await _repo.GetByIdAsync(saved.Id);
        loaded.Should().NotBeNull();
        loaded!.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task List_ReturnsPaginatedResults()
    {
        for (int i = 0; i < 5; i++)
        {
            await _repo.CreateAsync(new AuditEvent
            {

            });
        }

        var page = await _repo.ListAsync(0, 3);
        page.Should().HaveCount(3);

        var page2 = await _repo.ListAsync(3, 10);
        page2.Should().HaveCount(2);
    }

    [Fact]
    public async Task Update_ModifiesEntity()
    {
        var entity = new AuditEvent
        {

        };
        await _repo.CreateAsync(entity);

        entity.TenantId = "tenant-1";
        await _repo.UpdateAsync(entity);

        var loaded = await _repo.GetByIdAsync(entity.Id);
        loaded.Should().NotBeNull();
    }

    public void Dispose() => _db.Dispose();
}

file class TestTenantProvider : ITenantProvider
{
    public string TenantId { get; }
    public TestTenantProvider(string tenantId) => TenantId = tenantId;
}