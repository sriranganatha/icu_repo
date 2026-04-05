using Hms.RevenueService.Contracts;
using Hms.RevenueService.Data.Entities;
using Hms.RevenueService.Data.Repositories;
using Hms.RevenueService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.RevenueService.Services;

public sealed class ClaimService : IClaimService
{
    private readonly IClaimRepository _repo;
    private readonly RevenueServiceEventProducer _events;
    private readonly ILogger<ClaimService> _logger;

    public ClaimService(
        IClaimRepository repo,
        RevenueServiceEventProducer events,
        ILogger<ClaimService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<ClaimDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new ClaimDto
        {

        };
    }

    public async Task<List<ClaimDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200); // Performance: cap page size
            take = Math.Clamp(take, 1, 200); // Performance: cap page size
            var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new ClaimDto
        {

        }).ToList();
    }

    public async Task<ClaimDto> CreateAsync(CreateClaimRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Claim for tenant {Tenant}", request.TenantId);

        var entity = new Claim
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new ClaimCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created Claim {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new ClaimDto
        {

        };
    }

    public async Task<ClaimDto> UpdateAsync(UpdateClaimRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Claim {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Claim {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new ClaimUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new ClaimDto
        {

        };
    }
}