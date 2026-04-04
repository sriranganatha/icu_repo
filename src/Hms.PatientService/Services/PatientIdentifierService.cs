using Hms.PatientService.Contracts;
using Hms.PatientService.Data.Repositories;
using Hms.PatientService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.PatientService.Services;

public sealed class PatientIdentifierService : IPatientIdentifierService
{
    private readonly IPatientIdentifierRepository _repo;
    private readonly PatientServiceEventProducer _events;
    private readonly ILogger<PatientIdentifierService> _logger;

    public PatientIdentifierService(
        IPatientIdentifierRepository repo,
        PatientServiceEventProducer events,
        ILogger<PatientIdentifierService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<PatientIdentifierDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new PatientIdentifierDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<PatientIdentifierDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new PatientIdentifierDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<PatientIdentifierDto> CreateAsync(CreatePatientIdentifierRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating PatientIdentifier for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new PatientIdentifierDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new PatientIdentifierCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<PatientIdentifierDto> UpdateAsync(UpdatePatientIdentifierRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating PatientIdentifier {Id}", request.Id);
        await _events.PublishAsync(new PatientIdentifierUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new PatientIdentifierDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}