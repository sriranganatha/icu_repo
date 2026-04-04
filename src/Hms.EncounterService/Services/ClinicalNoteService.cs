using Hms.EncounterService.Contracts;
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
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<ClinicalNoteDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new ClinicalNoteDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<ClinicalNoteDto> CreateAsync(CreateClinicalNoteRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating ClinicalNote for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new ClinicalNoteDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new ClinicalNoteCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<ClinicalNoteDto> UpdateAsync(UpdateClinicalNoteRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating ClinicalNote {Id}", request.Id);
        await _events.PublishAsync(new ClinicalNoteUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new ClinicalNoteDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}