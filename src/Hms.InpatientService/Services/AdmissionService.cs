using Hms.InpatientService.Contracts;
using Hms.InpatientService.Data.Entities;
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

        };
    }

    public async Task<List<AdmissionDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200); // Performance: cap page size
            var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new AdmissionDto
        {

        }).ToList();
    }

    public async Task<AdmissionDto> CreateAsync(CreateAdmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Admission for tenant {Tenant}", request.TenantId);

        var entity = new Admission
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new AdmissionCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created Admission {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new AdmissionDto
        {

        };
    }

    public async Task<AdmissionDto> UpdateAsync(UpdateAdmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Admission {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Admission {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new AdmissionUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new AdmissionDto
        {

        };
    }
}