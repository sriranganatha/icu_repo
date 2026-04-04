using Hms.PatientService.Contracts;
using Hms.PatientService.Data.Repositories;
using Hms.PatientService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.PatientService.Services;

public sealed class PatientProfileService : IPatientProfileService
{
    private readonly IPatientProfileRepository _repo;
    private readonly PatientServiceEventProducer _events;
    private readonly ILogger<PatientProfileService> _logger;

    public PatientProfileService(
        IPatientProfileRepository repo,
        PatientServiceEventProducer events,
        ILogger<PatientProfileService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<PatientProfileDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new PatientProfileDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<PatientProfileDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new PatientProfileDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<PatientProfileDto> CreateAsync(CreatePatientProfileRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating PatientProfile for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new PatientProfileDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new PatientProfileCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<PatientProfileDto> UpdateAsync(UpdatePatientProfileRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating PatientProfile {Id}", request.Id);
        await _events.PublishAsync(new PatientProfileUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new PatientProfileDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}