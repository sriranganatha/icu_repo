using Hms.EmergencyService.Contracts;
using Hms.EmergencyService.Data.Entities;
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

        };
    }

    public async Task<List<EmergencyArrivalDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200); // Performance: cap page size
            take = Math.Clamp(take, 1, 200); // Performance: cap page size
            var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new EmergencyArrivalDto
        {

        }).ToList();
    }

    public async Task<EmergencyArrivalDto> CreateAsync(CreateEmergencyArrivalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating EmergencyArrival for tenant {Tenant}", request.TenantId);

        var entity = new EmergencyArrival
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new EmergencyArrivalCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created EmergencyArrival {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new EmergencyArrivalDto
        {

        };
    }

    public async Task<EmergencyArrivalDto> UpdateAsync(UpdateEmergencyArrivalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating EmergencyArrival {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"EmergencyArrival {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new EmergencyArrivalUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new EmergencyArrivalDto
        {

        };
    }
}