using Microsoft.EntityFrameworkCore;
using Hms.PatientService.Data;
using Hms.PatientService.Data.Entities;

namespace Hms.PatientService.Data.Repositories;

public interface IPatientProfileRepository
{
    Task<PatientProfile?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<PatientProfile>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<PatientProfile> CreateAsync(PatientProfile entity, CancellationToken ct = default);
    Task UpdateAsync(PatientProfile entity, CancellationToken ct = default);
}

public class PatientProfileRepository : IPatientProfileRepository
{
    private readonly PatientServiceDbContext _db;
    public PatientProfileRepository(PatientServiceDbContext db) => _db = db;

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