using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.EncounterService.Data;
using Hms.EncounterService.Data.Entities;
using Hms.EncounterService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for ClinicalNote using EF Core InMemory provider.
/// Feature coverage: EP-02, Module-D, ClinicalDocs
/// </summary>
public class ClinicalNoteRepositoryTests : IDisposable
{
    private readonly EncounterServiceDbContext _db;
    private readonly ClinicalNoteRepository _repo;

    public ClinicalNoteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<EncounterServiceDbContext>()
            .UseInMemoryDatabase($"ClinicalNote_3e1d98f98f0a46958557c73b108579fe")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new EncounterServiceDbContext(options, tenant);
        _repo = new ClinicalNoteRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new ClinicalNote
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
            await _repo.CreateAsync(new ClinicalNote
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
        var entity = new ClinicalNote
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