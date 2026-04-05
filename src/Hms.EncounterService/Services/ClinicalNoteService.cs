using Hms.EncounterService.Contracts;
using Hms.EncounterService.Data.Entities;
using Hms.EncounterService.Data.Repositories;
using Hms.EncounterService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.EncounterService.Services;

public sealed class ClinicalNoteService : IClinicalNoteService
{
    private readonly IClinicalNoteRepository _repo;
    private readonly EncounterServiceEventProducer _events;
    private readonly ILogger<ClinicalNoteService> _logger;

    public ClinicalNoteService(
        IClinicalNoteRepository repo,
        EncounterServiceEventProducer events,
        ILogger<ClinicalNoteService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<ClinicalNoteDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new ClinicalNoteDto
        {

        };
    }

    public async Task<List<ClinicalNoteDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new ClinicalNoteDto
        {

        }).ToList();
    }

    public async Task<ClinicalNoteDto> CreateAsync(CreateClinicalNoteRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating ClinicalNote for tenant {Tenant}", request.TenantId);

        var entity = new ClinicalNote
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new ClinicalNoteCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created ClinicalNote {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new ClinicalNoteDto
        {

        };
    }

    public async Task<ClinicalNoteDto> UpdateAsync(UpdateClinicalNoteRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating ClinicalNote {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"ClinicalNote {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new ClinicalNoteUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new ClinicalNoteDto
        {

        };
    }
}