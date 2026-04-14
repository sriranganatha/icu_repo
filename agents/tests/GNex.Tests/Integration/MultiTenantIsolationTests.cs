using GNex.Database;
using GNex.Database.Entities.Platform;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Entities.Platform.Technology;
using GNex.Database.Entities.Platform.LlmConfig;
using GNex.Database.Entities.Platform.AgentRegistry;
using GNex.Database.Repositories;
using FluentAssertions;

namespace GNex.Tests.Integration;

/// <summary>
/// Integration tests verifying multi-tenant data isolation.
/// Each tenant (organization) should only see their own data.
/// Tests use the shared InMemory database with different tenant providers
/// to simulate cross-tenant access attempts.
///
/// Scenarios: separate enterprise tenants (Acme Corp vs GNex Platform),
/// tenant-specific tech stacks, cross-tenant query isolation.
/// </summary>
public sealed class MultiTenantIsolationTests : IDisposable
{
    private readonly TestDbFixture _fix = new();

    [Fact]
    public async Task Project_TenantA_NotVisibleToTenantB()
    {
        var repoA = _fix.CreateRepo<Project>();
        await repoA.CreateAsync(new Project
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "GNex Internal Dashboard",
            Slug = "gnex-dashboard",
            ProjectType = "web_app",
            Status = "active"
        });

        // Create separate context for tenant B
        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<Project>(dbB);

        var tenantBProjects = await repoB.ListAsync(0, 100);
        tenantBProjects.Should().BeEmpty("Tenant B should not see Tenant A's projects");
    }

    [Fact]
    public async Task Language_CrossTenant_Isolation()
    {
        var repoA = _fix.CreateRepo<Language>();
        await repoA.CreateAsync(new Language
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "C#",
            Version = "12.0",
            Status = "active",
            FileExtensionsJson = """[".cs"]"""
        });

        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<Language>(dbB);

        await repoB.CreateAsync(new Language
        {
            TenantId = TestDbFixture.OtherTenantId,
            Name = "Python",
            Version = "3.12",
            Status = "active",
            FileExtensionsJson = """[".py"]"""
        });

        // Each tenant sees only their language
        var tenantALangs = await repoA.ListAsync(0, 100);
        tenantALangs.Should().HaveCount(1);
        tenantALangs.Should().OnlyContain(l => l.Name == "C#");

        var tenantBLangs = await repoB.ListAsync(0, 100);
        tenantBLangs.Should().HaveCount(1);
        tenantBLangs.Should().OnlyContain(l => l.Name == "Python");
    }

    [Fact]
    public async Task LlmModelConfig_TenantIsolation()
    {
        var repoA = _fix.CreateRepo<LlmModelConfig>();
        var providerIdA = Guid.NewGuid().ToString("N");
        await repoA.CreateAsync(new LlmModelConfig
        {
            TenantId = TestDbFixture.TestTenantId,
            ProviderId = providerIdA,
            ModelName = "gemini-2.5-pro",
            ContextWindow = 1_048_576,
            CostInputPer1kTokens = 0.00125m,
            CostOutputPer1kTokens = 0.005m
        });

        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<LlmModelConfig>(dbB);

        var tenantBModels = await repoB.ListAsync(0, 100);
        tenantBModels.Should().BeEmpty();
    }

    [Fact]
    public async Task Count_RespectsTenantBoundary()
    {
        var repoA = _fix.CreateRepo<Project>();
        await repoA.CreateAsync(new Project { TenantId = TestDbFixture.TestTenantId, Name = "Project A1", Slug = "proj-a1", ProjectType = "api", Status = "active" });
        await repoA.CreateAsync(new Project { TenantId = TestDbFixture.TestTenantId, Name = "Project A2", Slug = "proj-a2", ProjectType = "api", Status = "active" });

        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<Project>(dbB);
        await repoB.CreateAsync(new Project { TenantId = TestDbFixture.OtherTenantId, Name = "Project B1", Slug = "proj-b1", ProjectType = "web_app", Status = "active" });

        (await repoA.CountAsync()).Should().Be(2);
        (await repoB.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Query_FilterWithinTenant_Works()
    {
        var repoA = _fix.CreateRepo<Project>();
        await repoA.CreateAsync(new Project { TenantId = TestDbFixture.TestTenantId, Name = "Active API", Slug = "active-api", ProjectType = "api", Status = "active" });
        await repoA.CreateAsync(new Project { TenantId = TestDbFixture.TestTenantId, Name = "Paused Web", Slug = "paused-web", ProjectType = "web_app", Status = "paused" });

        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<Project>(dbB);
        await repoB.CreateAsync(new Project { TenantId = TestDbFixture.OtherTenantId, Name = "Active Mobile", Slug = "active-mobile", ProjectType = "mobile", Status = "active" });

        var activeA = await repoA.QueryAsync(p => p.Status == "active");
        activeA.Should().HaveCount(1);
        activeA.First().Name.Should().Be("Active API");

        var activeB = await repoB.QueryAsync(p => p.Status == "active");
        activeB.Should().HaveCount(1);
        activeB.First().Name.Should().Be("Active Mobile");
    }

    [Fact]
    public async Task SoftDelete_IsolatedPerTenant()
    {
        var repoA = _fix.CreateRepo<Project>();
        var projA = await repoA.CreateAsync(new Project
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Deletable A",
            Slug = "deletable-a",
            ProjectType = "api",
            Status = "active"
        });

        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<Project>(dbB);
        var projB = await repoB.CreateAsync(new Project
        {
            TenantId = TestDbFixture.OtherTenantId,
            Name = "Deletable B",
            Slug = "deletable-b",
            ProjectType = "api",
            Status = "active"
        });

        // Soft delete tenant A's project
        await repoA.SoftDeleteAsync(projA.Id);

        // Tenant A sees 0 projects
        (await repoA.CountAsync()).Should().Be(0);

        // Tenant B still sees their project
        (await repoB.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AgentLearning_CrossTenant_FullIsolation()
    {
        var repoA = _fix.CreateRepo<AgentLearning>();
        await repoA.CreateAsync(new AgentLearning
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = "proj-tenant-a",
            AgentTypeCode = "CodeGenerator",
            Category = "code_quality",
            Problem = "Tenant A specific: Payment gateway retry logic missing",
            Resolution = "Add exponential backoff retry",
            Impact = "high",
            TargetAgents = "CodeGenerator",
            PromptRule = "Always implement retry for payment calls",
            Recurrence = 1,
            Domain = "fintech",
            Scope = 0,
            Confidence = 0.3,
            SeenInProjects = "proj-tenant-a",
            SeenInDomains = "fintech"
        });

        using var dbB = _fix.CreateDbForTenant(TestDbFixture.OtherTenantId);
        var repoB = new PlatformRepository<AgentLearning>(dbB);

        var tenantBLearnings = await repoB.ListAsync(0, 100);
        tenantBLearnings.Should().BeEmpty("Tenant B should never see Tenant A's agent learnings");
    }

    public void Dispose() => _fix.Dispose();
}
