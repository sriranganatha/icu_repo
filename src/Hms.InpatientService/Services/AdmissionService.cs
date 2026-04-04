using Hms.InpatientService.Contracts;
using Hms.InpatientService.Data.Repositories;
using Hms.InpatientService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.InpatientService.Services;

public sealed class AdmissionService : IAdmissionService
{
    private readonly IAdmissionRepository _repo;
    private readonly InpatientServiceEventProducer _events;
    private readonly ILogger<AdmissionService> _logger;

    public AdmissionService(
        IAdmissionRepository repo,
        InpatientServiceEventProducer events,
        ILogger<AdmissionService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<AdmissionDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new AdmissionDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<AdmissionDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new AdmissionDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<AdmissionDto> CreateAsync(CreateAdmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Admission for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new AdmissionDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new AdmissionCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<AdmissionDto> UpdateAsync(UpdateAdmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Admission {Id}", request.Id);
        await _events.PublishAsync(new AdmissionUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new AdmissionDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}