using Hms.EmergencyService.Contracts;
using Hms.EmergencyService.Data.Repositories;
using Hms.EmergencyService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.EmergencyService.Services;

public sealed class EmergencyArrivalService : IEmergencyArrivalService
{
    private readonly IEmergencyArrivalRepository _repo;
    private readonly EmergencyServiceEventProducer _events;
    private readonly ILogger<EmergencyArrivalService> _logger;

    public EmergencyArrivalService(
        IEmergencyArrivalRepository repo,
        EmergencyServiceEventProducer events,
        ILogger<EmergencyArrivalService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<EmergencyArrivalDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new EmergencyArrivalDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<EmergencyArrivalDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new EmergencyArrivalDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<EmergencyArrivalDto> CreateAsync(CreateEmergencyArrivalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating EmergencyArrival for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new EmergencyArrivalDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new EmergencyArrivalCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<EmergencyArrivalDto> UpdateAsync(UpdateEmergencyArrivalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating EmergencyArrival {Id}", request.Id);
        await _events.PublishAsync(new EmergencyArrivalUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new EmergencyArrivalDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}