using Microsoft.EntityFrameworkCore;
using Hms.Database;

namespace Hms.Database.Repositories;

public interface IPatientRepository
{
    Task<PatientProfile?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<PatientProfile>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<PatientProfile> CreateAsync(PatientProfile entity, CancellationToken ct = default);
    Task UpdateAsync(PatientProfile entity, CancellationToken ct = default);
}

public class PatientRepository : IPatientRepository
{
    private readonly HmsDbContext _db;

    public PatientRepository(HmsDbContext db) => _db = db;

    public async Task<PatientProfile?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<PatientProfile>().FindAsync([id], ct);

    public async Task<List<PatientProfile>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<PatientProfile>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<PatientProfile> CreateAsync(PatientProfile entity, CancellationToken ct = default)
    {
        _db.Set<PatientProfile>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(PatientProfile entity, CancellationToken ct = default)
    {
        _db.Set<PatientProfile>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}