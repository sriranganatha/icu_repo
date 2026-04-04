using Microsoft.EntityFrameworkCore;
using Hms.EncounterService.Data;
using Hms.EncounterService.Data.Entities;

namespace Hms.EncounterService.Data.Repositories;

public interface IClinicalNoteRepository
{
    Task<ClinicalNote?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ClinicalNote>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ClinicalNote> CreateAsync(ClinicalNote entity, CancellationToken ct = default);
    Task UpdateAsync(ClinicalNote entity, CancellationToken ct = default);
}

public class ClinicalNoteRepository : IClinicalNoteRepository
{
    private readonly EncounterServiceDbContext _db;
    public ClinicalNoteRepository(EncounterServiceDbContext db) => _db = db;

    public async Task<ClinicalNote?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<ClinicalNote>().FindAsync([id], ct);

    public async Task<List<ClinicalNote>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<ClinicalNote>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<ClinicalNote> CreateAsync(ClinicalNote entity, CancellationToken ct = default)
    {
        _db.Set<ClinicalNote>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(ClinicalNote entity, CancellationToken ct = default)
    {
        _db.Set<ClinicalNote>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}