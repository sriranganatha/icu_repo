using Microsoft.EntityFrameworkCore;
using GNex.Database.Entities.Platform.Projects;

namespace GNex.Database.Repositories;

public interface IProjectRepository : IPlatformRepository<Project>
{
    Task<Project?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Project?> GetWithDetailsAsync(string id, CancellationToken ct = default);
    Task<List<Project>> ListByStatusAsync(string status, int skip = 0, int take = 50, CancellationToken ct = default);
}

public class ProjectRepository : PlatformRepository<Project>, IProjectRepository
{
    public ProjectRepository(GNexDbContext db) : base(db) { }

    public async Task<Project?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive, ct);

    public async Task<Project?> GetWithDetailsAsync(string id, CancellationToken ct = default)
        => await Set
            .Include(p => p.Settings)
            .Include(p => p.TechStack)
            .Include(p => p.Environments)
            .Include(p => p.TeamMembers)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<List<Project>> ListByStatusAsync(string status, int skip = 0, int take = 50, CancellationToken ct = default)
        => await Set.Where(p => p.IsActive && p.Status == status)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);
}
