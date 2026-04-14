using GNex.Database;
using GNex.Database.Entities.Platform.Configuration;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Repositories;
using GNex.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GNex.Tests.Integration;

/// <summary>
/// Integration tests for ConfigResolverService exercising:
///   3-tier config resolution (Master → Org → Project), snapshot lifecycle,
///   tech stack inclusion, and configuration merge semantics.
///
/// Scenarios: fintech compliance-heavy project, SaaS with custom deployment, minimal project.
/// </summary>
public sealed class ConfigResolverIntegrationTests : IDisposable
{
    private readonly TestDbFixture _fix = new();
    private readonly ConfigResolverService _service;

    public ConfigResolverIntegrationTests()
    {
        var snapshotRepo = _fix.CreateRepo<ConfigSnapshot>();
        _service = new ConfigResolverService(_fix.Db, snapshotRepo, NullLogger<ConfigResolverService>.Instance);
    }

    [Fact]
    public async Task ResolveConfig_MinimalProject_ReturnsMasterDefaults()
    {
        var project = await SeedProject("Minimal MVP", "minimal-mvp");

        var config = await _service.ResolveConfigAsync(project.Id);

        config.Should().NotBeNull();
        // Master defaults
        config["quality"]?["testCoverageThreshold"]?.GetValue<int>().Should().Be(80);
        config["quality"]?["codeReviewRequired"]?.GetValue<bool>().Should().BeTrue();
        config["deployment"]?["strategy"]?.GetValue<string>().Should().Be("blue_green");
        config["agents"]?["maxRetries"]?.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public async Task ResolveConfig_WithTechStack_IncludesTechStackArray()
    {
        var project = await SeedProject("E-Commerce Platform", "ecommerce-platform");
        var tsRepo = _fix.CreateRepo<ProjectTechStack>();

        await tsRepo.CreateAsync(new ProjectTechStack
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Layer = "backend",
            TechnologyId = "lang-csharp",
            TechnologyType = "language",
            Version = "12.0"
        });
        await tsRepo.CreateAsync(new ProjectTechStack
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Layer = "frontend",
            TechnologyId = "fw-react",
            TechnologyType = "framework",
            Version = "19.0"
        });

        var config = await _service.ResolveConfigAsync(project.Id);

        var techStack = config["techStack"]?.AsArray();
        techStack.Should().NotBeNull();
        techStack!.Count.Should().Be(2);
        techStack[0]!["layer"]?.GetValue<string>().Should().Be("backend");
        techStack[1]!["layer"]?.GetValue<string>().Should().Be("frontend");
    }

    [Fact]
    public async Task ResolveConfig_WithProjectSettings_IncludesProjectSection()
    {
        var project = await SeedProject("Analytics Dashboard", "analytics-dash");
        var settingsRepo = _fix.CreateRepo<ProjectSettings>();

        await settingsRepo.CreateAsync(new ProjectSettings
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            GitRepoUrl = "https://github.com/gnex/analytics-dash",
            DefaultBranch = "develop",
            ArtifactStoragePath = "output/analytics-dash"
        });

        var config = await _service.ResolveConfigAsync(project.Id);

        var projectSection = config["project"];
        projectSection.Should().NotBeNull();
        projectSection!["gitRepoUrl"]?.GetValue<string>().Should().Be("https://github.com/gnex/analytics-dash");
        projectSection["defaultBranch"]?.GetValue<string>().Should().Be("develop");
    }

    [Fact]
    public async Task ResolveConfig_WithOrgOverrides_MergesOnTopOfDefaults()
    {
        var project = await SeedProject("Fintech Compliance", "fintech-compliance");
        var settingsRepo = _fix.CreateRepo<ProjectSettings>();

        // Org overrides via NotificationConfigJson
        await settingsRepo.CreateAsync(new ProjectSettings
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            GitRepoUrl = "https://github.com/fintech-co/compliance-engine",
            DefaultBranch = "main",
            NotificationConfigJson = """{"quality":{"testCoverageThreshold":95},"deployment":{"strategy":"canary","requireApproval":true}}"""
        });

        var config = await _service.ResolveConfigAsync(project.Id);

        // Org override applied
        config["quality"]?["testCoverageThreshold"]?.GetValue<int>().Should().Be(95);
        config["deployment"]?["strategy"]?.GetValue<string>().Should().Be("canary");
        config["deployment"]?["requireApproval"]?.GetValue<bool>().Should().BeTrue();

        // Master defaults NOT overridden should remain
        config["quality"]?["codeReviewRequired"]?.GetValue<bool>().Should().BeTrue();
        config["agents"]?["maxRetries"]?.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public async Task ResolveConfig_NonExistentProject_ReturnsEmptyJson()
    {
        var config = await _service.ResolveConfigAsync("nonexistent-project-id");
        config.Should().NotBeNull();
        config.Count.Should().Be(0);
    }

    [Fact]
    public async Task CreateSnapshot_PersistsResolvedConfig()
    {
        var project = await SeedProject("Snapshot Project", "snapshot-proj");
        var tsRepo = _fix.CreateRepo<ProjectTechStack>();

        await tsRepo.CreateAsync(new ProjectTechStack
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Layer = "backend",
            TechnologyId = "lang-python",
            TechnologyType = "language",
            Version = "3.12"
        });

        var snapshot = await _service.CreateSnapshotAsync(project.Id, "pre_deploy", "Sprint 1 release");

        snapshot.Should().NotBeNull();
        snapshot.ProjectId.Should().Be(project.Id);
        snapshot.SnapshotType.Should().Be("pre_deploy");
        snapshot.TriggerReason.Should().Be("Sprint 1 release");
        snapshot.ConfigJson.Should().Contain("lang-python");
    }

    [Fact]
    public async Task GetSnapshot_ReturnsPersistedSnapshot()
    {
        var project = await SeedProject("Get Snapshot Project", "get-snapshot");
        var snapshot = await _service.CreateSnapshotAsync(project.Id, "manual", "Testing retrieval");

        var retrieved = await _service.GetSnapshotAsync(snapshot.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(snapshot.Id);
        retrieved.ConfigJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListSnapshots_ReturnsOrderedByDate()
    {
        var project = await SeedProject("List Snapshots Project", "list-snapshots");

        await _service.CreateSnapshotAsync(project.Id, "pre_deploy", "Release 1.0");
        await _service.CreateSnapshotAsync(project.Id, "post_deploy", "Release 1.0 verification");
        await _service.CreateSnapshotAsync(project.Id, "pre_deploy", "Release 1.1");

        var snapshots = await _service.ListSnapshotsAsync(project.Id);

        snapshots.Should().HaveCount(3);
        snapshots.First().TriggerReason.Should().Be("Release 1.1");
    }

    [Fact]
    public async Task ListSnapshots_RespectLimit()
    {
        var project = await SeedProject("Limited Snapshots", "limited-snapshots");
        for (int i = 0; i < 5; i++)
            await _service.CreateSnapshotAsync(project.Id, "auto", $"Snapshot {i}");

        var snapshots = await _service.ListSnapshotsAsync(project.Id, limit: 3);
        snapshots.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateSnapshot_MultipleSnapshots_IndependentInstances()
    {
        var project = await SeedProject("Independent Snapshots", "independent-snapshots");
        var settingsRepo = _fix.CreateRepo<ProjectSettings>();
        var tsRepo = _fix.CreateRepo<ProjectTechStack>();

        await settingsRepo.CreateAsync(new ProjectSettings
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            DefaultBranch = "main"
        });

        // Snapshot 1: before tech stack change
        var snap1 = await _service.CreateSnapshotAsync(project.Id, "pre_change", "Before adding frontend");

        // Add tech stack
        await tsRepo.CreateAsync(new ProjectTechStack
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Layer = "frontend",
            TechnologyId = "fw-angular",
            TechnologyType = "framework",
            Version = "18.0"
        });

        // Snapshot 2: after tech stack change
        var snap2 = await _service.CreateSnapshotAsync(project.Id, "post_change", "After adding frontend");

        // Snapshots should differ
        snap1.ConfigJson.Should().NotBe(snap2.ConfigJson);
        snap2.ConfigJson.Should().Contain("fw-angular");
    }

    // ── Helpers ──

    private async Task<Project> SeedProject(string name, string slug)
    {
        var repo = _fix.CreateRepo<Project>();
        return await repo.CreateAsync(new Project
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = name,
            Slug = slug,
            ProjectType = "web_app",
            Status = "active"
        });
    }

    public void Dispose() => _fix.Dispose();
}
