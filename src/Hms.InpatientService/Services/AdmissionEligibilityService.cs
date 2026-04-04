using Hms.InpatientService.Contracts;
using Hms.InpatientService.Data.Repositories;
using Hms.InpatientService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.InpatientService.Services;

public sealed class AdmissionEligibilityService : IAdmissionEligibilityService
{
    private readonly IAdmissionEligibilityRepository _repo;
    private readonly InpatientServiceEventProducer _events;
    private readonly ILogger<AdmissionEligibilityService> _logger;

    public AdmissionEligibilityService(
        IAdmissionEligibilityRepository repo,
        InpatientServiceEventProducer events,
        ILogger<AdmissionEligibilityService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<AdmissionEligibilityDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new AdmissionEligibilityDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<AdmissionEligibilityDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new AdmissionEligibilityDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<AdmissionEligibilityDto> CreateAsync(CreateAdmissionEligibilityRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AdmissionEligibility for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new AdmissionEligibilityDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new AdmissionEligibilityCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<AdmissionEligibilityDto> UpdateAsync(UpdateAdmissionEligibilityRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AdmissionEligibility {Id}", request.Id);
        await _events.PublishAsync(new AdmissionEligibilityUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new AdmissionEligibilityDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}