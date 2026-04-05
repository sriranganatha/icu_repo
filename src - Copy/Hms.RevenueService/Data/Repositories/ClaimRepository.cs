using Microsoft.EntityFrameworkCore;
using Hms.RevenueService.Data;
using Hms.RevenueService.Data.Entities;

namespace Hms.RevenueService.Data.Repositories;

public interface IClaimRepository
{
    Task<Claim?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<Claim>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<Claim> CreateAsync(Claim entity, CancellationToken ct = default);
    Task UpdateAsync(Claim entity, CancellationToken ct = default);
}

public class ClaimRepository : IClaimRepository
{
    private readonly RevenueServiceDbContext _db;
    public ClaimRepository(RevenueServiceDbContext db) => _db = db;

    public async Task<Claim?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<Claim>().FindAsync([id], ct);

    public async Task<List<Claim>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<Claim>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<Claim> CreateAsync(Claim entity, CancellationToken ct = default)
    {
        _db.Set<Claim>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(Claim entity, CancellationToken ct = default)
    {
        _db.Set<Claim>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}