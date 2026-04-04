using Microsoft.EntityFrameworkCore;
using Hms.Database;

namespace Hms.Database.Repositories;

public interface IAuditRepository
{
    Task<AuditEvent?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AuditEvent>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AuditEvent> CreateAsync(AuditEvent entity, CancellationToken ct = default);
    Task UpdateAsync(AuditEvent entity, CancellationToken ct = default);
}

public class AuditRepository : IAuditRepository
{
    private readonly HmsDbContext _db;

    public AuditRepository(HmsDbContext db) => _db = db;

    public async Task<AuditEvent?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<AuditEvent>().FindAsync([id], ct);

    public async Task<List<AuditEvent>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<AuditEvent>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<AuditEvent> CreateAsync(AuditEvent entity, CancellationToken ct = default)
    {
        _db.Set<AuditEvent>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(AuditEvent entity, CancellationToken ct = default)
    {
        _db.Set<AuditEvent>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}