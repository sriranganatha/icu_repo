using Microsoft.EntityFrameworkCore;
using Hms.EmergencyService.Data;
using Hms.EmergencyService.Data.Entities;

namespace Hms.EmergencyService.Data.Repositories;

public interface ITriageAssessmentRepository
{
    Task<TriageAssessment?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<TriageAssessment>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<TriageAssessment> CreateAsync(TriageAssessment entity, CancellationToken ct = default);
    Task UpdateAsync(TriageAssessment entity, CancellationToken ct = default);
}

public class TriageAssessmentRepository : ITriageAssessmentRepository
{
    private readonly EmergencyServiceDbContext _db;
    public TriageAssessmentRepository(EmergencyServiceDbContext db) => _db = db;

    public async Task<TriageAssessment?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<TriageAssessment>().FindAsync([id], ct);

    public async Task<List<TriageAssessment>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<TriageAssessment>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<TriageAssessment> CreateAsync(TriageAssessment entity, CancellationToken ct = default)
    {
        _db.Set<TriageAssessment>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(TriageAssessment entity, CancellationToken ct = default)
    {
        _db.Set<TriageAssessment>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}