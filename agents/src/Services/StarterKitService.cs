using Hms.Database;
using Hms.Database.Entities.Platform.Configuration;
using Hms.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hms.Services.Platform;

/// <summary>Manages starter kits for quick project creation.</summary>
public interface IStarterKitService
{
    Task<List<StarterKit>> ListAsync(CancellationToken ct = default);
    Task<StarterKit?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<StarterKit> CreateAsync(StarterKit kit, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public class StarterKitService(
    IPlatformRepository<StarterKit> repo,
    ILogger<StarterKitService> logger) : IStarterKitService
{
    public async Task<List<StarterKit>> ListAsync(CancellationToken ct = default)
        => await repo.ListAsync(ct: ct);

    public async Task<StarterKit?> GetByIdAsync(string id, CancellationToken ct = default)
        => await repo.GetByIdAsync(id, ct);

    public async Task<StarterKit> CreateAsync(StarterKit kit, CancellationToken ct = default)
    {
        await repo.CreateAsync(kit, ct);
        logger.LogInformation("Created starter kit {Name}", kit.Name);
        return kit;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
        => await repo.SoftDeleteAsync(id, ct);
}
