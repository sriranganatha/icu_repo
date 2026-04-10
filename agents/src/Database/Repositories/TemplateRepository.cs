using Microsoft.EntityFrameworkCore;
using Hms.Database.Entities.Platform;
using Hms.Database.Entities.Platform.Technology;

namespace Hms.Database.Repositories;

public interface ITemplateRepository
{
    Task<List<T>> ListTemplatesAsync<T>(int skip = 0, int take = 50, CancellationToken ct = default) where T : PlatformEntityBase;
    Task<T?> GetTemplateByIdAsync<T>(string id, CancellationToken ct = default) where T : PlatformEntityBase;
    Task<List<CodeTemplate>> GetCodeTemplatesByLanguageAsync(string languageId, CancellationToken ct = default);
    Task<List<DockerTemplate>> GetDockerTemplatesByFrameworkAsync(string frameworkId, CancellationToken ct = default);
}

public class TemplateRepository : ITemplateRepository
{
    private readonly HmsDbContext _db;

    public TemplateRepository(HmsDbContext db) => _db = db;

    public async Task<List<T>> ListTemplatesAsync<T>(int skip = 0, int take = 50, CancellationToken ct = default) where T : PlatformEntityBase
        => await _db.Set<T>().Where(e => e.IsActive)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<T?> GetTemplateByIdAsync<T>(string id, CancellationToken ct = default) where T : PlatformEntityBase
        => await _db.Set<T>().FindAsync([id], ct);

    public async Task<List<CodeTemplate>> GetCodeTemplatesByLanguageAsync(string languageId, CancellationToken ct = default)
        => await _db.CodeTemplates.Where(t => t.LanguageId == languageId && t.IsActive)
            .OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<List<DockerTemplate>> GetDockerTemplatesByFrameworkAsync(string frameworkId, CancellationToken ct = default)
        => await _db.DockerTemplates.Where(t => t.FrameworkId == frameworkId && t.IsActive)
            .OrderBy(t => t.Name).ToListAsync(ct);
}
