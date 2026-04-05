using Microsoft.EntityFrameworkCore;
using Hms.AuditService.Data;
using Hms.AuditService.Data.Entities;

namespace Hms.AuditService.Data.Repositories;

public interface IAuditEventRepository
{
    Task<AuditEvent?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AuditEvent>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AuditEvent> CreateAsync(AuditEvent entity, CancellationToken ct = default);
    Task UpdateAsync(AuditEvent entity, CancellationToken ct = default);
}

public class AuditEventRepository : IAuditEventRepository
{
    private readonly AuditServiceDbContext _db;
    public AuditEventRepository(AuditServiceDbContext db) => _db = db;

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