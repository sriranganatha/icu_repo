using Hms.InpatientService.Contracts;
using Hms.InpatientService.Data.Entities;
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

        };
    }

    public async Task<List<AdmissionEligibilityDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new AdmissionEligibilityDto
        {

        }).ToList();
    }

    public async Task<AdmissionEligibilityDto> CreateAsync(CreateAdmissionEligibilityRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AdmissionEligibility for tenant {Tenant}", request.TenantId);

        var entity = new AdmissionEligibility
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new AdmissionEligibilityCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created AdmissionEligibility {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new AdmissionEligibilityDto
        {

        };
    }

    public async Task<AdmissionEligibilityDto> UpdateAsync(UpdateAdmissionEligibilityRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AdmissionEligibility {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"AdmissionEligibility {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new AdmissionEligibilityUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new AdmissionEligibilityDto
        {

        };
    }
}