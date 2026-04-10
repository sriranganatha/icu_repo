using Hms.Database;
using Hms.Database.Entities.Platform.Technology;
using Hms.Database.Repositories;
using Hms.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hms.Services.Platform;

public sealed class TemplateService : ITemplateService
{
    private readonly IPlatformRepository<BrdTemplate> _brdRepo;
    private readonly IPlatformRepository<ArchitectureTemplate> _archRepo;
    private readonly IPlatformRepository<CodeTemplate> _codeRepo;
    private readonly IPlatformRepository<FileStructureTemplate> _fsRepo;
    private readonly IPlatformRepository<CiCdTemplate> _cicdRepo;
    private readonly IPlatformRepository<DockerTemplate> _dockerRepo;
    private readonly IPlatformRepository<TestTemplate> _testRepo;
    private readonly IPlatformRepository<IaCTemplate> _iacRepo;
    private readonly IPlatformRepository<DocumentationTemplate> _docRepo;
    private readonly HmsDbContext _db;
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(
        IPlatformRepository<BrdTemplate> brdRepo,
        IPlatformRepository<ArchitectureTemplate> archRepo,
        IPlatformRepository<CodeTemplate> codeRepo,
        IPlatformRepository<FileStructureTemplate> fsRepo,
        IPlatformRepository<CiCdTemplate> cicdRepo,
        IPlatformRepository<DockerTemplate> dockerRepo,
        IPlatformRepository<TestTemplate> testRepo,
        IPlatformRepository<IaCTemplate> iacRepo,
        IPlatformRepository<DocumentationTemplate> docRepo,
        HmsDbContext db,
        ILogger<TemplateService> logger)
    {
        _brdRepo = brdRepo;
        _archRepo = archRepo;
        _codeRepo = codeRepo;
        _fsRepo = fsRepo;
        _cicdRepo = cicdRepo;
        _dockerRepo = dockerRepo;
        _testRepo = testRepo;
        _iacRepo = iacRepo;
        _docRepo = docRepo;
        _db = db;
        _logger = logger;
    }

    // ─── BRD Templates ──────────────────────────────────────
    public async Task<List<BrdTemplateDto>> ListBrdTemplatesAsync(CancellationToken ct = default)
    {
        var items = await _brdRepo.ListAsync(ct: ct);
        return items.Select(t => new BrdTemplateDto(
            t.Id, t.Name, t.ProjectType, t.SectionsJson, t.IsDefault,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<BrdTemplateDto> CreateBrdTemplateAsync(CreateBrdTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new BrdTemplate
        {
            Name = request.Name,
            ProjectType = request.ProjectType,
            SectionsJson = request.SectionsJson,
            IsDefault = request.IsDefault
        };
        await _brdRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created BRD template {Id} '{Name}'", entity.Id, entity.Name);
        return new BrdTemplateDto(entity.Id, entity.Name, entity.ProjectType,
            entity.SectionsJson, entity.IsDefault, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteBrdTemplateAsync(string id, CancellationToken ct = default)
        => await _brdRepo.SoftDeleteAsync(id, ct);

    // ─── Architecture Templates ─────────────────────────────
    public async Task<List<ArchitectureTemplateDto>> ListArchitectureTemplatesAsync(CancellationToken ct = default)
    {
        var items = await _archRepo.ListAsync(ct: ct);
        return items.Select(t => new ArchitectureTemplateDto(
            t.Id, t.Name, t.Pattern, t.DiagramTemplate,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<ArchitectureTemplateDto> CreateArchitectureTemplateAsync(CreateArchitectureTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new ArchitectureTemplate
        {
            Name = request.Name,
            Pattern = request.Pattern,
            DiagramTemplate = request.DiagramTemplate
        };
        await _archRepo.CreateAsync(entity, ct);
        return new ArchitectureTemplateDto(entity.Id, entity.Name, entity.Pattern,
            entity.DiagramTemplate, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteArchitectureTemplateAsync(string id, CancellationToken ct = default)
        => await _archRepo.SoftDeleteAsync(id, ct);

    // ─── Code Templates ─────────────────────────────────────
    public async Task<List<CodeTemplateDto>> ListCodeTemplatesAsync(string? languageId = null, string? frameworkId = null, CancellationToken ct = default)
    {
        var query = _db.CodeTemplates.Where(t => t.IsActive);
        if (languageId is not null)
            query = query.Where(t => t.LanguageId == languageId);
        if (frameworkId is not null)
            query = query.Where(t => t.FrameworkId == frameworkId);
        var items = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return items.Select(MapCodeTemplate).ToList();
    }

    public async Task<CodeTemplateDto> CreateCodeTemplateAsync(CreateCodeTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new CodeTemplate
        {
            Name = request.Name,
            LanguageId = request.LanguageId,
            FrameworkId = request.FrameworkId,
            TemplateType = request.TemplateType,
            Content = request.Content,
            VariablesJson = request.VariablesJson
        };
        await _codeRepo.CreateAsync(entity, ct);
        return MapCodeTemplate(entity);
    }

    public async Task DeleteCodeTemplateAsync(string id, CancellationToken ct = default)
        => await _codeRepo.SoftDeleteAsync(id, ct);

    // ─── File Structure Templates ───────────────────────────
    public async Task<List<FileStructureTemplateDto>> ListFileStructureTemplatesAsync(string? frameworkId = null, CancellationToken ct = default)
    {
        List<FileStructureTemplate> items;
        if (frameworkId is not null)
            items = await _fsRepo.QueryAsync(t => t.FrameworkId == frameworkId, ct: ct);
        else
            items = await _fsRepo.ListAsync(ct: ct);
        return items.Select(t => new FileStructureTemplateDto(
            t.Id, t.Name, t.FrameworkId, t.TreeJson,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<FileStructureTemplateDto> CreateFileStructureTemplateAsync(CreateFileStructureTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new FileStructureTemplate
        {
            Name = request.Name,
            FrameworkId = request.FrameworkId,
            TreeJson = request.TreeJson
        };
        await _fsRepo.CreateAsync(entity, ct);
        return new FileStructureTemplateDto(entity.Id, entity.Name, entity.FrameworkId,
            entity.TreeJson, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteFileStructureTemplateAsync(string id, CancellationToken ct = default)
        => await _fsRepo.SoftDeleteAsync(id, ct);

    // ─── CI/CD Templates ────────────────────────────────────
    public async Task<List<CiCdTemplateDto>> ListCiCdTemplatesAsync(string? provider = null, CancellationToken ct = default)
    {
        List<CiCdTemplate> items;
        if (provider is not null)
            items = await _cicdRepo.QueryAsync(t => t.Provider == provider, ct: ct);
        else
            items = await _cicdRepo.ListAsync(ct: ct);
        return items.Select(t => new CiCdTemplateDto(
            t.Id, t.Name, t.Provider, t.LanguageId, t.PipelineYaml,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<CiCdTemplateDto> CreateCiCdTemplateAsync(CreateCiCdTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new CiCdTemplate
        {
            Name = request.Name,
            Provider = request.Provider,
            LanguageId = request.LanguageId,
            PipelineYaml = request.PipelineYaml
        };
        await _cicdRepo.CreateAsync(entity, ct);
        return new CiCdTemplateDto(entity.Id, entity.Name, entity.Provider,
            entity.LanguageId, entity.PipelineYaml, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteCiCdTemplateAsync(string id, CancellationToken ct = default)
        => await _cicdRepo.SoftDeleteAsync(id, ct);

    // ─── Docker Templates ───────────────────────────────────
    public async Task<List<DockerTemplateDto>> ListDockerTemplatesAsync(string? frameworkId = null, CancellationToken ct = default)
    {
        var query = _db.DockerTemplates.Where(t => t.IsActive);
        if (frameworkId is not null)
            query = query.Where(t => t.FrameworkId == frameworkId);
        var items = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return items.Select(t => new DockerTemplateDto(
            t.Id, t.Name, t.LanguageId, t.FrameworkId,
            t.DockerfileContent, t.ComposeContent,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<DockerTemplateDto> CreateDockerTemplateAsync(CreateDockerTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new DockerTemplate
        {
            Name = request.Name,
            LanguageId = request.LanguageId,
            FrameworkId = request.FrameworkId,
            DockerfileContent = request.DockerfileContent,
            ComposeContent = request.ComposeContent
        };
        await _dockerRepo.CreateAsync(entity, ct);
        return new DockerTemplateDto(entity.Id, entity.Name, entity.LanguageId,
            entity.FrameworkId, entity.DockerfileContent, entity.ComposeContent,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteDockerTemplateAsync(string id, CancellationToken ct = default)
        => await _dockerRepo.SoftDeleteAsync(id, ct);

    // ─── Test Templates ─────────────────────────────────────
    public async Task<List<TestTemplateDto>> ListTestTemplatesAsync(string? frameworkId = null, CancellationToken ct = default)
    {
        List<TestTemplate> items;
        if (frameworkId is not null)
            items = await _testRepo.QueryAsync(t => t.FrameworkId == frameworkId, ct: ct);
        else
            items = await _testRepo.ListAsync(ct: ct);
        return items.Select(t => new TestTemplateDto(
            t.Id, t.Name, t.TestType, t.FrameworkId, t.TestFramework,
            t.TemplateContent, t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<TestTemplateDto> CreateTestTemplateAsync(CreateTestTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new TestTemplate
        {
            Name = request.Name,
            TestType = request.TestType,
            FrameworkId = request.FrameworkId,
            TestFramework = request.TestFramework,
            TemplateContent = request.TemplateContent
        };
        await _testRepo.CreateAsync(entity, ct);
        return new TestTemplateDto(entity.Id, entity.Name, entity.TestType,
            entity.FrameworkId, entity.TestFramework, entity.TemplateContent,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteTestTemplateAsync(string id, CancellationToken ct = default)
        => await _testRepo.SoftDeleteAsync(id, ct);

    // ─── IaC Templates ──────────────────────────────────────
    public async Task<List<IaCTemplateDto>> ListIaCTemplatesAsync(string? tool = null, CancellationToken ct = default)
    {
        List<IaCTemplate> items;
        if (tool is not null)
            items = await _iacRepo.QueryAsync(t => t.Tool == tool, ct: ct);
        else
            items = await _iacRepo.ListAsync(ct: ct);
        return items.Select(t => new IaCTemplateDto(
            t.Id, t.Name, t.CloudProviderId, t.Tool, t.TemplateContent,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<IaCTemplateDto> CreateIaCTemplateAsync(CreateIaCTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new IaCTemplate
        {
            Name = request.Name,
            CloudProviderId = request.CloudProviderId,
            Tool = request.Tool,
            TemplateContent = request.TemplateContent
        };
        await _iacRepo.CreateAsync(entity, ct);
        return new IaCTemplateDto(entity.Id, entity.Name, entity.CloudProviderId,
            entity.Tool, entity.TemplateContent, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteIaCTemplateAsync(string id, CancellationToken ct = default)
        => await _iacRepo.SoftDeleteAsync(id, ct);

    // ─── Documentation Templates ────────────────────────────
    public async Task<List<DocumentationTemplateDto>> ListDocumentationTemplatesAsync(string? docType = null, CancellationToken ct = default)
    {
        List<DocumentationTemplate> items;
        if (docType is not null)
            items = await _docRepo.QueryAsync(t => t.DocType == docType, ct: ct);
        else
            items = await _docRepo.ListAsync(ct: ct);
        return items.Select(t => new DocumentationTemplateDto(
            t.Id, t.Name, t.DocType, t.TemplateContent,
            t.IsActive, t.CreatedAt, t.UpdatedAt)).ToList();
    }

    public async Task<DocumentationTemplateDto> CreateDocumentationTemplateAsync(CreateDocumentationTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new DocumentationTemplate
        {
            Name = request.Name,
            DocType = request.DocType,
            TemplateContent = request.TemplateContent
        };
        await _docRepo.CreateAsync(entity, ct);
        return new DocumentationTemplateDto(entity.Id, entity.Name, entity.DocType,
            entity.TemplateContent, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task DeleteDocumentationTemplateAsync(string id, CancellationToken ct = default)
        => await _docRepo.SoftDeleteAsync(id, ct);

    // ─── Mapping Helpers ────────────────────────────────────
    private static CodeTemplateDto MapCodeTemplate(CodeTemplate t) => new(
        t.Id, t.Name, t.LanguageId, t.FrameworkId, t.TemplateType,
        t.Content, t.VariablesJson, t.IsActive, t.CreatedAt, t.UpdatedAt);
}
