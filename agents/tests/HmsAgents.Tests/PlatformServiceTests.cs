using FluentAssertions;
using Hms.Database;
using Hms.Database.Entities.Platform.Technology;
using Hms.Database.Entities.Platform.Projects;
using Hms.Database.Entities.Platform.Workflows;
using Hms.Database.Entities.Platform.LlmConfig;
using Hms.Database.Repositories;
using Hms.Services.Dtos.Platform;
using Hms.Services.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HmsAgents.Tests;

/// <summary>
/// Service layer tests for platform services (TechnologyService, WorkflowService, etc.).
/// Uses in-memory EF Core via real repositories.
/// </summary>
public class PlatformServiceTests : IDisposable
{
    private readonly HmsDbContext _db;

    public PlatformServiceTests()
    {
        var options = new DbContextOptionsBuilder<HmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new HmsDbContext(options, new TestTenantProvider("svc-test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── TechnologyService ──

    [Fact]
    public async Task TechnologyService_CreateLanguage_ReturnsDto()
    {
        var svc = CreateTechnologyService();

        var dto = await svc.CreateLanguageAsync(new CreateLanguageRequest
        {
            Name = "C#",
            Version = "12"
        });

        dto.Should().NotBeNull();
        dto.Name.Should().Be("C#");
    }

    [Fact]
    public async Task TechnologyService_ListLanguages_ReturnsPaginated()
    {
        var svc = CreateTechnologyService();

        await svc.CreateLanguageAsync(new CreateLanguageRequest { Name = "C#", Version = "12" });
        await svc.CreateLanguageAsync(new CreateLanguageRequest { Name = "Java", Version = "21" });
        await svc.CreateLanguageAsync(new CreateLanguageRequest { Name = "Python", Version = "3.12" });

        var list = await svc.ListLanguagesAsync(0, 2);

        list.Should().HaveCount(2);
    }

    // ── WorkflowService ──

    [Fact]
    public async Task WorkflowService_CreateWorkflow()
    {
        var svc = CreateWorkflowService();

        var dto = await svc.CreateWorkflowAsync(new CreateWorkflowRequest(
            "Agile SDLC", "Standard agile workflow", true));

        dto.Should().NotBeNull();
        dto.Name.Should().Be("Agile SDLC");
    }

    [Fact]
    public async Task WorkflowService_AddStage()
    {
        var svc = CreateWorkflowService();

        var wf = await svc.CreateWorkflowAsync(new CreateWorkflowRequest(
            "Test WF", "Test", false));
        var stage = await svc.AddStageAsync(new CreateStageRequest(
            wf.Id, "Requirements", 1, null, null, "[\"RequirementsReader\"]"));

        stage.Should().NotBeNull();
        stage.Name.Should().Be("Requirements");
        stage.Order.Should().Be(1);
    }

    // ── ProjectManagementService ──

    [Fact]
    public async Task ProjectManagementService_CreateProject()
    {
        var svc = CreateProjectManagementService();

        var dto = await svc.CreateAsync(new CreateProjectRequest
        {
            Name = "HMS",
            ProjectType = "web_app",
            Description = "Hospital Management"
        });

        dto.Should().NotBeNull();
        dto.Name.Should().Be("HMS");
        dto.Status.Should().Be("draft");
    }

    [Fact]
    public async Task ProjectManagementService_AddTechStack()
    {
        var svc = CreateProjectManagementService();

        var project = await svc.CreateAsync(new CreateProjectRequest
        {
            Name = "Test",
            ProjectType = "api",
            Description = "Test proj"
        });

        var stack = await svc.AddTechStackAsync(new AddTechStackRequest
        {
            ProjectId = project.Id,
            Layer = "backend",
            TechnologyId = "fw1",
            TechnologyType = "framework",
            Version = "9.0"
        });

        stack.Should().NotBeNull();
        stack.Layer.Should().Be("backend");
        stack.TechnologyType.Should().Be("framework");
    }

    // ── LlmConfigService ──

    [Fact]
    public async Task LlmConfigService_CreateProvider()
    {
        var svc = CreateLlmConfigService();

        var dto = await svc.CreateProviderAsync(new CreateLlmProviderRequest(
            "Google Gemini", "https://api.gemini.google.com", "api_key", 60));

        dto.Should().NotBeNull();
        dto.Name.Should().Be("Google Gemini");
    }

    [Fact]
    public async Task LlmConfigService_CreateModel()
    {
        var svc = CreateLlmConfigService();

        var provider = await svc.CreateProviderAsync(new CreateLlmProviderRequest(
            "Test", "https://api.test.com", "api_key", 60));
        var model = await svc.AddModelAsync(new CreateLlmModelRequest(
            provider.Id, "test-model", 4096, 0.01m, 0.03m, "[]"));

        model.Should().NotBeNull();
        model.ModelName.Should().Be("test-model");
        model.ContextWindow.Should().Be(4096);
    }

    // ── Helpers ──

    private TechnologyService CreateTechnologyService()
    {
        return new TechnologyService(
            new PlatformRepository<Language>(_db),
            new PlatformRepository<Framework>(_db),
            new PlatformRepository<DatabaseTechnology>(_db),
            new PlatformRepository<CloudProvider>(_db),
            new PlatformRepository<DevOpsTool>(_db),
            new Mock<ILogger<TechnologyService>>().Object);
    }

    private WorkflowService CreateWorkflowService()
    {
        return new WorkflowService(
            new PlatformRepository<SdlcWorkflow>(_db),
            _db,
            new Mock<ILogger<WorkflowService>>().Object);
    }

    private ProjectManagementService CreateProjectManagementService()
    {
        return new ProjectManagementService(
            new ProjectRepository(_db),
            _db,
            new Mock<ILogger<ProjectManagementService>>().Object);
    }

    private LlmConfigService CreateLlmConfigService()
    {
        return new LlmConfigService(
            new PlatformRepository<LlmProviderConfig>(_db),
            _db,
            new Mock<ILogger<LlmConfigService>>().Object);
    }

    private sealed class TestTenantProvider : ITenantProvider
    {
        public string TenantId { get; }
        public TestTenantProvider(string tenantId) => TenantId = tenantId;
    }
}
