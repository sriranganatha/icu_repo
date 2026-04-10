using Microsoft.EntityFrameworkCore;
using GNex.Database.Entities.Platform.AgentRegistry;

namespace GNex.Database.Repositories;

public interface IAgentRegistryRepository : IPlatformRepository<AgentTypeDefinition>
{
    Task<AgentTypeDefinition?> GetWithConfigAsync(string id, CancellationToken ct = default);
    Task<AgentTypeDefinition?> GetByAgentTypeCodeAsync(string agentTypeCode, CancellationToken ct = default);
    Task<List<AgentTypeDefinition>> ListWithMappingsAsync(int skip = 0, int take = 50, CancellationToken ct = default);
}

public class AgentRegistryRepository : PlatformRepository<AgentTypeDefinition>, IAgentRegistryRepository
{
    public AgentRegistryRepository(GNexDbContext db) : base(db) { }

    public async Task<AgentTypeDefinition?> GetWithConfigAsync(string id, CancellationToken ct = default)
        => await Set
            .Include(a => a.ModelMappings)
            .Include(a => a.Tools)
            .Include(a => a.Prompts)
            .Include(a => a.Constraints)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<AgentTypeDefinition?> GetByAgentTypeCodeAsync(string agentTypeCode, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(a => a.AgentTypeCode == agentTypeCode && a.IsActive, ct);

    public async Task<List<AgentTypeDefinition>> ListWithMappingsAsync(int skip = 0, int take = 50, CancellationToken ct = default)
        => await Set.Where(a => a.IsActive)
            .Include(a => a.ModelMappings)
            .OrderBy(a => a.Name)
            .Skip(skip).Take(take).ToListAsync(ct);
}
