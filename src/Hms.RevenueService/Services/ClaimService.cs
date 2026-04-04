using Hms.RevenueService.Contracts;
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
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<ClaimDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new ClaimDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<ClaimDto> CreateAsync(CreateClaimRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Claim for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new ClaimDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new ClaimCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<ClaimDto> UpdateAsync(UpdateClaimRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Claim {Id}", request.Id);
        await _events.PublishAsync(new ClaimUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new ClaimDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}