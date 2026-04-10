using Microsoft.EntityFrameworkCore;
using GNex.Database;

namespace GNex.Database.Repositories;

public interface IEncounterRepository
{
    Task<Encounter?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<Encounter>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<Encounter> CreateAsync(Encounter entity, CancellationToken ct = default);
    Task UpdateAsync(Encounter entity, CancellationToken ct = default);
}

public class EncounterRepository : IEncounterRepository
{
    private readonly GNexDbContext _db;

    public EncounterRepository(GNexDbContext db) => _db = db;

    public async Task<Encounter?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<Encounter>().FindAsync([id], ct);

    public async Task<List<Encounter>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<Encounter>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<Encounter> CreateAsync(Encounter entity, CancellationToken ct = default)
    {
        _db.Set<Encounter>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(Encounter entity, CancellationToken ct = default)
    {
        _db.Set<Encounter>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}