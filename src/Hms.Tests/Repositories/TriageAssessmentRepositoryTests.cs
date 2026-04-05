using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.EmergencyService.Data;
using Hms.EmergencyService.Data.Entities;
using Hms.EmergencyService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for TriageAssessment using EF Core InMemory provider.
/// Feature coverage: EP-03, Module-E, Emergency
/// </summary>
public class TriageAssessmentRepositoryTests : IDisposable
{
    private readonly EmergencyServiceDbContext _db;
    private readonly TriageAssessmentRepository _repo;

    public TriageAssessmentRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<EmergencyServiceDbContext>()
            .UseInMemoryDatabase($"TriageAssessment_963492caf7704e36be1123054be56d23")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new EmergencyServiceDbContext(options, tenant);
        _repo = new TriageAssessmentRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new TriageAssessment
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
            await _repo.CreateAsync(new TriageAssessment
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
        var entity = new TriageAssessment
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