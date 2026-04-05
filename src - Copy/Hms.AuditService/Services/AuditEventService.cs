using Hms.AuditService.Contracts;
using Hms.AuditService.Data.Entities;
using Hms.AuditService.Data.Repositories;
using Hms.AuditService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.AuditService.Services;

public sealed class AuditEventService : IAuditEventService
{
    private readonly IAuditEventRepository _repo;
    private readonly AuditServiceEventProducer _events;
    private readonly ILogger<AuditEventService> _logger;

    public AuditEventService(
        IAuditEventRepository repo,
        AuditServiceEventProducer events,
        ILogger<AuditEventService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<AuditEventDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new AuditEventDto
        {

        };
    }

    public async Task<List<AuditEventDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200); // Performance: cap page size
            var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new AuditEventDto
        {

        }).ToList();
    }

    public async Task<AuditEventDto> CreateAsync(CreateAuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AuditEvent for tenant {Tenant}", request.TenantId);

        var entity = new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new AuditEventCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created AuditEvent {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new AuditEventDto
        {

        };
    }

    public async Task<AuditEventDto> UpdateAsync(UpdateAuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AuditEvent {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"AuditEvent {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new AuditEventUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new AuditEventDto
        {

        };
    }
}