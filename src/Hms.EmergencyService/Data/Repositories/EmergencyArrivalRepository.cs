using Microsoft.EntityFrameworkCore;
using Hms.EmergencyService.Data;
using Hms.EmergencyService.Data.Entities;

namespace Hms.EmergencyService.Data.Repositories;

public interface IEmergencyArrivalRepository
{
    Task<EmergencyArrival?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<EmergencyArrival>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<EmergencyArrival> CreateAsync(EmergencyArrival entity, CancellationToken ct = default);
    Task UpdateAsync(EmergencyArrival entity, CancellationToken ct = default);
}

public class EmergencyArrivalRepository : IEmergencyArrivalRepository
{
    private readonly EmergencyServiceDbContext _db;
    public EmergencyArrivalRepository(EmergencyServiceDbContext db) => _db = db;

    public async Task<EmergencyArrival?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<EmergencyArrival>().FindAsync([id], ct);

    public async Task<List<EmergencyArrival>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<EmergencyArrival>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<EmergencyArrival> CreateAsync(EmergencyArrival entity, CancellationToken ct = default)
    {
        _db.Set<EmergencyArrival>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(EmergencyArrival entity, CancellationToken ct = default)
    {
        _db.Set<EmergencyArrival>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}