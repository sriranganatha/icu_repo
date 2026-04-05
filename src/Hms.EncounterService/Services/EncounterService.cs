using Hms.EncounterService.Contracts;
using Hms.EncounterService.Data.Entities;
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

        };
    }

    public async Task<List<EncounterDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new EncounterDto
        {

        }).ToList();
    }

    public async Task<EncounterDto> CreateAsync(CreateEncounterRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Encounter for tenant {Tenant}", request.TenantId);

        var entity = new Encounter
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new EncounterCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created Encounter {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new EncounterDto
        {

        };
    }

    public async Task<EncounterDto> UpdateAsync(UpdateEncounterRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Encounter {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Encounter {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new EncounterUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new EncounterDto
        {

        };
    }
}