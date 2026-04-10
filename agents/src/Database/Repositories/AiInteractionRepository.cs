using Microsoft.EntityFrameworkCore;
using GNex.Database;

namespace GNex.Database.Repositories;

public interface IAiInteractionRepository
{
    Task<AiInteraction?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AiInteraction>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AiInteraction> CreateAsync(AiInteraction entity, CancellationToken ct = default);
    Task UpdateAsync(AiInteraction entity, CancellationToken ct = default);
}

public class AiInteractionRepository : IAiInteractionRepository
{
    private readonly GNexDbContext _db;

    public AiInteractionRepository(GNexDbContext db) => _db = db;

    public async Task<AiInteraction?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _db.Set<AiInteraction>().FindAsync([id], ct);

    public async Task<List<AiInteraction>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await _db.Set<AiInteraction>().OrderByDescending(e => e.CreatedAt)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<AiInteraction> CreateAsync(AiInteraction entity, CancellationToken ct = default)
    {
        _db.Set<AiInteraction>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(AiInteraction entity, CancellationToken ct = default)
    {
        _db.Set<AiInteraction>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }
}