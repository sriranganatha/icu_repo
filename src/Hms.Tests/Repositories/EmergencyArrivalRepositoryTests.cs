using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.EmergencyService.Data;
using Hms.EmergencyService.Data.Entities;
using Hms.EmergencyService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for EmergencyArrival using EF Core InMemory provider.
/// Feature coverage: EP-03, Module-E, Emergency
/// </summary>
public class EmergencyArrivalRepositoryTests : IDisposable
{
    private readonly EmergencyServiceDbContext _db;
    private readonly EmergencyArrivalRepository _repo;

    public EmergencyArrivalRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<EmergencyServiceDbContext>()
            .UseInMemoryDatabase($"EmergencyArrival_bd2e7645456346f785bcbc30c644bdc3")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new EmergencyServiceDbContext(options, tenant);
        _repo = new EmergencyArrivalRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new EmergencyArrival
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
            await _repo.CreateAsync(new EmergencyArrival
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
        var entity = new EmergencyArrival
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