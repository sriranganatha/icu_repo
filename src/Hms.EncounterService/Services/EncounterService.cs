using Hms.EncounterService.Contracts;
using Hms.EncounterService.Data.Repositories;
using Hms.EncounterService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.EncounterService.Services;

public sealed class EncounterService : IEncounterService
{
    private readonly IEncounterRepository _repo;
    private readonly EncounterServiceEventProducer _events;
    private readonly ILogger<EncounterService> _logger;

    public EncounterService(
        IEncounterRepository repo,
        EncounterServiceEventProducer events,
        ILogger<EncounterService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<EncounterDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new EncounterDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<EncounterDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new EncounterDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<EncounterDto> CreateAsync(CreateEncounterRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Encounter for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new EncounterDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new EncounterCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<EncounterDto> UpdateAsync(UpdateEncounterRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Encounter {Id}", request.Id);
        await _events.PublishAsync(new EncounterUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new EncounterDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}