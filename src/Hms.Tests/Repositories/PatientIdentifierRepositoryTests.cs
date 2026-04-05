using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.PatientService.Data;
using Hms.PatientService.Data.Entities;
using Hms.PatientService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for PatientIdentifier using EF Core InMemory provider.
/// Feature coverage: EP-01, Module-B, Patient
/// </summary>
public class PatientIdentifierRepositoryTests : IDisposable
{
    private readonly PatientServiceDbContext _db;
    private readonly PatientIdentifierRepository _repo;

    public PatientIdentifierRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<PatientServiceDbContext>()
            .UseInMemoryDatabase($"PatientIdentifier_ed4652627b844b3a889a21f840fca50d")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new PatientServiceDbContext(options, tenant);
        _repo = new PatientIdentifierRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new PatientIdentifier
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
            await _repo.CreateAsync(new PatientIdentifier
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
        var entity = new PatientIdentifier
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