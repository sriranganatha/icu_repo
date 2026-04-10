using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Hms.Database.Entities.Platform;

namespace Hms.Database.Repositories;

public interface IPlatformRepository<T> where T : PlatformEntityBase
{
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<T>> ListAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<List<T>> QueryAsync(Expression<Func<T, bool>> predicate, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task<T> CreateAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task SoftDeleteAsync(string id, CancellationToken ct = default);
    Task RestoreAsync(string id, CancellationToken ct = default);
}

public class PlatformRepository<T> : IPlatformRepository<T> where T : PlatformEntityBase
{
    protected readonly HmsDbContext Db;
    protected DbSet<T> Set => Db.Set<T>();

    public PlatformRepository(HmsDbContext db) => Db = db;

    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public async Task<List<T>> ListAsync(int skip = 0, int take = 50, CancellationToken ct = default)
        => await Set.Where(e => e.IsActive)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<List<T>> QueryAsync(Expression<Func<T, bool>> predicate, int skip = 0, int take = 50, CancellationToken ct = default)
        => await Set.Where(e => e.IsActive).Where(predicate)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        IQueryable<T> query = Set.Where(e => e.IsActive);
        if (predicate != null) query = query.Where(predicate);
        return await query.CountAsync(ct);
    }

    public async Task<T> CreateAsync(T entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(entity.TenantId))
            entity.TenantId = Db.CurrentTenantId;
        Set.Add(entity);
        await Db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.VersionNo++;
        Set.Update(entity);
        await Db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await Set.FindAsync([id], ct);
        if (entity is null) return;
        entity.IsActive = false;
        entity.ArchivedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(string id, CancellationToken ct = default)
    {
        var entity = await Set.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return;
        entity.IsActive = true;
        entity.ArchivedAt = null;
        await Db.SaveChangesAsync(ct);
    }
}
