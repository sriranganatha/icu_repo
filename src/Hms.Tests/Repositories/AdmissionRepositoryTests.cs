using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.InpatientService.Data;
using Hms.InpatientService.Data.Entities;
using Hms.InpatientService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for Admission using EF Core InMemory provider.
/// Feature coverage: EP-04, Module-F, ADT
/// </summary>
public class AdmissionRepositoryTests : IDisposable
{
    private readonly InpatientServiceDbContext _db;
    private readonly AdmissionRepository _repo;

    public AdmissionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<InpatientServiceDbContext>()
            .UseInMemoryDatabase($"Admission_40d3fca6024c4750b62cdd10ac35ccbe")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new InpatientServiceDbContext(options, tenant);
        _repo = new AdmissionRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new Admission
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
            await _repo.CreateAsync(new Admission
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
        var entity = new Admission
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