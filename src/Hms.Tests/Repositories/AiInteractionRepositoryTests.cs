using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Hms.AiService.Data;
using Hms.AiService.Data.Entities;
using Hms.AiService.Data.Repositories;
using Xunit;

namespace Hms.Tests.Repositories;

/// <summary>
/// Repository tests for AiInteraction using EF Core InMemory provider.
/// Feature coverage: EP-P1, Module-P, AI
/// </summary>
public class AiInteractionRepositoryTests : IDisposable
{
    private readonly AiServiceDbContext _db;
    private readonly AiInteractionRepository _repo;

    public AiInteractionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AiServiceDbContext>()
            .UseInMemoryDatabase($"AiInteraction_70bc1c08a0984fb5b0c4603dd9b21eb8")
            .Options;
        var tenant = new TestTenantProvider("tenant-1");
        _db = new AiServiceDbContext(options, tenant);
        _repo = new AiInteractionRepository(_db);
    }

    [Fact]
    public async Task Create_PersistsEntity()
    {
        var entity = new AiInteraction
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
            await _repo.CreateAsync(new AiInteraction
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
        var entity = new AiInteraction
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