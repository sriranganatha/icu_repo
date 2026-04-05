using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.RevenueService.Data;
using Hms.RevenueService.Data.Entities;
using Hms.RevenueService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for Claim using EF Core InMemory provider.
/// Feature coverage: EP-12, Module-L, Revenue
/// </summary>
public class ClaimRepositoryTests : IDisposable
{
    private readonly RevenueServiceDbContext _db;
    private readonly ClaimRepository _repo;

    public ClaimRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<RevenueServiceDbContext>()
            .UseInMemoryDatabase($"Claim_1506d88fe5934147a5dda95532bb26a3")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new RevenueServiceDbContext(options, tenant);
        _repo = new ClaimRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new Claim
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
            await _repo.CreateAsync(new Claim
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
        var entity = new Claim
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