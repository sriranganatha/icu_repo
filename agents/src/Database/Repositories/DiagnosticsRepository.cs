using Microsoft.EntityFrameworkCore;
using GNex.Database;

namespace GNex.Database.Repositories;

public interface IDiagnosticsRepository
{
    Task<ResultRecord?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ResultRecord>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ResultRecord> CreateAsync(ResultRecord entity, CancellationToken ct = default);
    Task UpdateAsync(ResultRecord entity, CancellationToken ct = default);
}

public class DiagnosticsRepository : IDiagnosticsRepository
{
    private readonly GNexDbContext _db;

    public DiagnosticsRepository(GNexDbContext db) => _db = db;

    public async Task<ResultRecord?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<ResultRecord>().FindAsync([id], ct);

    public async Task<List<ResultRecord>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<ResultRecord>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<ResultRecord> CreateAsync(ResultRecord entity, CancellationToken ct = default)
    {
        _db.Set<ResultRecord>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(ResultRecord entity, CancellationToken ct = default)
    {
        _db.Set<ResultRecord>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}