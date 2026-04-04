using Microsoft.EntityFrameworkCore;
using Hms.PatientService.Data;
using Hms.PatientService.Data.Entities;

namespace Hms.PatientService.Data.Repositories;

public interface IPatientIdentifierRepository
{
    Task<PatientIdentifier?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<PatientIdentifier>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<PatientIdentifier> CreateAsync(PatientIdentifier entity, CancellationToken ct = default);
    Task UpdateAsync(PatientIdentifier entity, CancellationToken ct = default);
}

public class PatientIdentifierRepository : IPatientIdentifierRepository
{
    private readonly PatientServiceDbContext _db;
    public PatientIdentifierRepository(PatientServiceDbContext db) => _db = db;

    public async Task<PatientIdentifier?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<PatientIdentifier>().FindAsync([id], ct);

    public async Task<List<PatientIdentifier>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<PatientIdentifier>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<PatientIdentifier> CreateAsync(PatientIdentifier entity, CancellationToken ct = default)
    {
        _db.Set<PatientIdentifier>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(PatientIdentifier entity, CancellationToken ct = default)
    {
        _db.Set<PatientIdentifier>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}