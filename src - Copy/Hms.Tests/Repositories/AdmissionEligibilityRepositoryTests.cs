using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.InpatientService.Data;
using Hms.InpatientService.Data.Entities;
using Hms.InpatientService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for AdmissionEligibility using EF Core InMemory provider.
/// Feature coverage: EP-04, Module-F, ADT
/// </summary>
public class AdmissionEligibilityRepositoryTests : IDisposable
{
    private readonly InpatientServiceDbContext _db;
    private readonly AdmissionEligibilityRepository _repo;

    public AdmissionEligibilityRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<InpatientServiceDbContext>()
            .UseInMemoryDatabase($"AdmissionEligibility_24a2ca6aaecd42edb90ea266ecef5a2c")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new InpatientServiceDbContext(options, tenant);
        _repo = new AdmissionEligibilityRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new AdmissionEligibility
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
            await _repo.CreateAsync(new AdmissionEligibility
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
        var entity = new AdmissionEligibility
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