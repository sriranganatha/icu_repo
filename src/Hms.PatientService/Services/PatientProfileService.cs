using Hms.PatientService.Contracts;
using Hms.PatientService.Data.Entities;
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

        };
    }

    public async Task<List<PatientProfileDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new PatientProfileDto
        {

        }).ToList();
    }

    public async Task<PatientProfileDto> CreateAsync(CreatePatientProfileRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating PatientProfile for tenant {Tenant}", request.TenantId);

        var entity = new PatientProfile
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new PatientProfileCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created PatientProfile {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new PatientProfileDto
        {

        };
    }

    public async Task<PatientProfileDto> UpdateAsync(UpdatePatientProfileRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating PatientProfile {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"PatientProfile {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new PatientProfileUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new PatientProfileDto
        {

        };
    }
}