using Microsoft.EntityFrameworkCore;
using Hms.InpatientService.Data;
using Hms.InpatientService.Data.Entities;

namespace Hms.InpatientService.Data.Repositories;

public interface IAdmissionEligibilityRepository
{
    Task<AdmissionEligibility?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AdmissionEligibility>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AdmissionEligibility> CreateAsync(AdmissionEligibility entity, CancellationToken ct = default);
    Task UpdateAsync(AdmissionEligibility entity, CancellationToken ct = default);
}

public class AdmissionEligibilityRepository : IAdmissionEligibilityRepository
{
    private readonly InpatientServiceDbContext _db;
    public AdmissionEligibilityRepository(InpatientServiceDbContext db) => _db = db;

    public async Task<AdmissionEligibility?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<AdmissionEligibility>().FindAsync([id], ct);

    public async Task<List<AdmissionEligibility>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<AdmissionEligibility>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<AdmissionEligibility> CreateAsync(AdmissionEligibility entity, CancellationToken ct = default)
    {
        _db.Set<AdmissionEligibility>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(AdmissionEligibility entity, CancellationToken ct = default)
    {
        _db.Set<AdmissionEligibility>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}