using GNex.Database;
using GNex.Database.Entities.Platform.Technology;
using GNex.Database.Repositories;
using GNex.Services.Dtos.Platform;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

public sealed class TechnologyService : ITechnologyService
{
    private readonly IPlatformRepository<Language> _languageRepo;
    private readonly IPlatformRepository<Framework> _frameworkRepo;
    private readonly IPlatformRepository<DatabaseTechnology> _dbTechRepo;
    private readonly IPlatformRepository<CloudProvider> _cloudRepo;
    private readonly IPlatformRepository<DevOpsTool> _devOpsRepo;
    private readonly ILogger<TechnologyService> _logger;

    public TechnologyService(
        IPlatformRepository<Language> languageRepo,
        IPlatformRepository<Framework> frameworkRepo,
        IPlatformRepository<DatabaseTechnology> dbTechRepo,
        IPlatformRepository<CloudProvider> cloudRepo,
        IPlatformRepository<DevOpsTool> devOpsRepo,
        ILogger<TechnologyService> logger)
    {
        _languageRepo = languageRepo;
        _frameworkRepo = frameworkRepo;
        _dbTechRepo = dbTechRepo;
        _cloudRepo = cloudRepo;
        _devOpsRepo = devOpsRepo;
        _logger = logger;
    }

    // ── Languages ─────────────────────────────────────────
    public async Task<LanguageDto?> GetLanguageAsync(string id, CancellationToken ct = default)
    {
        var e = await _languageRepo.GetByIdAsync(id, ct);
        return e is null ? null : MapLanguage(e);
    }

    public async Task<List<LanguageDto>> ListLanguagesAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var items = await _languageRepo.ListAsync(skip, take, ct);
        return items.Select(MapLanguage).ToList();
    }

    public async Task<LanguageDto> CreateLanguageAsync(CreateLanguageRequest request, CancellationToken ct = default)
    {
        var entity = new Language
        {
            Name = request.Name,
            Version = request.Version,
            Icon = request.Icon,
            FileExtensionsJson = request.FileExtensionsJson
        };
        await _languageRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created language {Name} v{Version}", entity.Name, entity.Version);
        return MapLanguage(entity);
    }

    public async Task<LanguageDto> UpdateLanguageAsync(UpdateLanguageRequest request, CancellationToken ct = default)
    {
        var entity = await _languageRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Language {request.Id} not found");
        if (request.Name is not null) entity.Name = request.Name;
        if (request.Version is not null) entity.Version = request.Version;
        if (request.Status is not null) entity.Status = request.Status;
        if (request.Icon is not null) entity.Icon = request.Icon;
        if (request.FileExtensionsJson is not null) entity.FileExtensionsJson = request.FileExtensionsJson;
        await _languageRepo.UpdateAsync(entity, ct);
        return MapLanguage(entity);
    }

    public Task DeleteLanguageAsync(string id, CancellationToken ct = default)
        => _languageRepo.SoftDeleteAsync(id, ct);

    // ── Frameworks ────────────────────────────────────────
    public async Task<FrameworkDto?> GetFrameworkAsync(string id, CancellationToken ct = default)
    {
        var e = await _frameworkRepo.GetByIdAsync(id, ct);
        return e is null ? null : MapFramework(e);
    }

    public async Task<List<FrameworkDto>> ListFrameworksAsync(string? languageId = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var items = languageId is not null
            ? await _frameworkRepo.QueryAsync(f => f.LanguageId == languageId, skip, take, ct)
            : await _frameworkRepo.ListAsync(skip, take, ct);
        return items.Select(MapFramework).ToList();
    }

    public async Task<FrameworkDto> CreateFrameworkAsync(CreateFrameworkRequest request, CancellationToken ct = default)
    {
        var entity = new Framework
        {
            Name = request.Name,
            LanguageId = request.LanguageId,
            Version = request.Version,
            Category = request.Category,
            DocsUrl = request.DocsUrl
        };
        await _frameworkRepo.CreateAsync(entity, ct);
        return MapFramework(entity);
    }

    public Task DeleteFrameworkAsync(string id, CancellationToken ct = default)
        => _frameworkRepo.SoftDeleteAsync(id, ct);

    // ── Databases ─────────────────────────────────────────
    public async Task<List<DatabaseTechnologyDto>> ListDatabasesAsync(CancellationToken ct = default)
    {
        var items = await _dbTechRepo.ListAsync(0, 100, ct);
        return items.Select(e => new DatabaseTechnologyDto
        {
            Id = e.Id, Name = e.Name, DbType = e.DbType,
            DefaultPort = e.DefaultPort, ConnectionTemplate = e.ConnectionTemplate
        }).ToList();
    }

    public async Task<DatabaseTechnologyDto> CreateDatabaseAsync(CreateDatabaseTechnologyRequest request, CancellationToken ct = default)
    {
        var entity = new DatabaseTechnology
        {
            Name = request.Name, DbType = request.DbType,
            DefaultPort = request.DefaultPort, ConnectionTemplate = request.ConnectionTemplate
        };
        await _dbTechRepo.CreateAsync(entity, ct);
        return new DatabaseTechnologyDto
        {
            Id = entity.Id, Name = entity.Name, DbType = entity.DbType,
            DefaultPort = entity.DefaultPort, ConnectionTemplate = entity.ConnectionTemplate
        };
    }

    // ── Cloud Providers ───────────────────────────────────
    public async Task<List<CloudProviderDto>> ListCloudProvidersAsync(CancellationToken ct = default)
    {
        var items = await _cloudRepo.ListAsync(0, 100, ct);
        return items.Select(e => new CloudProviderDto
        {
            Id = e.Id, Name = e.Name, RegionsJson = e.RegionsJson, ServicesJson = e.ServicesJson
        }).ToList();
    }

    public async Task<CloudProviderDto> CreateCloudProviderAsync(CreateCloudProviderRequest request, CancellationToken ct = default)
    {
        var entity = new CloudProvider { Name = request.Name, RegionsJson = request.RegionsJson, ServicesJson = request.ServicesJson };
        await _cloudRepo.CreateAsync(entity, ct);
        return new CloudProviderDto { Id = entity.Id, Name = entity.Name, RegionsJson = entity.RegionsJson, ServicesJson = entity.ServicesJson };
    }

    // ── DevOps Tools ──────────────────────────────────────
    public async Task<List<DevOpsToolDto>> ListDevOpsToolsAsync(CancellationToken ct = default)
    {
        var items = await _devOpsRepo.ListAsync(0, 100, ct);
        return items.Select(e => new DevOpsToolDto
        {
            Id = e.Id, Name = e.Name, Category = e.Category, ConfigTemplate = e.ConfigTemplate
        }).ToList();
    }

    public async Task<DevOpsToolDto> CreateDevOpsToolAsync(CreateDevOpsToolRequest request, CancellationToken ct = default)
    {
        var entity = new DevOpsTool { Name = request.Name, Category = request.Category, ConfigTemplate = request.ConfigTemplate };
        await _devOpsRepo.CreateAsync(entity, ct);
        return new DevOpsToolDto { Id = entity.Id, Name = entity.Name, Category = entity.Category, ConfigTemplate = entity.ConfigTemplate };
    }

    // ── Mappers ───────────────────────────────────────────
    private static LanguageDto MapLanguage(Language e) => new()
    {
        Id = e.Id, Name = e.Name, Version = e.Version, Status = e.Status,
        Icon = e.Icon, FileExtensionsJson = e.FileExtensionsJson, CreatedAt = e.CreatedAt
    };

    private static FrameworkDto MapFramework(Framework e) => new()
    {
        Id = e.Id, Name = e.Name, LanguageId = e.LanguageId,
        Version = e.Version, Category = e.Category, DocsUrl = e.DocsUrl
    };
}
