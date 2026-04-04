using Microsoft.EntityFrameworkCore;
using Hms.Database;

namespace Hms.Database.Repositories;

public interface IAdmissionRepository
{
    Task<Admission?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<Admission>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<Admission> CreateAsync(Admission entity, CancellationToken ct = default);
    Task UpdateAsync(Admission entity, CancellationToken ct = default);
}

public class AdmissionRepository : IAdmissionRepository
{
    private readonly HmsDbContext _db;

    public AdmissionRepository(HmsDbContext db) => _db = db;

    public async Task<Admission?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<Admission>().FindAsync([id], ct);

    public async Task<List<Admission>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<Admission>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<Admission> CreateAsync(Admission entity, CancellationToken ct = default)
    {
        _db.Set<Admission>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(Admission entity, CancellationToken ct = default)
    {
        _db.Set<Admission>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}