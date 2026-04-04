using Hms.AuditService.Contracts;
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
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<AuditEventDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new AuditEventDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<AuditEventDto> CreateAsync(CreateAuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AuditEvent for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new AuditEventDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new AuditEventCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<AuditEventDto> UpdateAsync(UpdateAuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AuditEvent {Id}", request.Id);
        await _events.PublishAsync(new AuditEventUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new AuditEventDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}