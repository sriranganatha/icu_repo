using Hms.PatientService.Contracts;
using Hms.PatientService.Data.Entities;
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

        };
    }

    public async Task<List<PatientIdentifierDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new PatientIdentifierDto
        {

        }).ToList();
    }

    public async Task<PatientIdentifierDto> CreateAsync(CreatePatientIdentifierRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating PatientIdentifier for tenant {Tenant}", request.TenantId);

        var entity = new PatientIdentifier
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new PatientIdentifierCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created PatientIdentifier {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new PatientIdentifierDto
        {

        };
    }

    public async Task<PatientIdentifierDto> UpdateAsync(UpdatePatientIdentifierRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating PatientIdentifier {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"PatientIdentifier {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new PatientIdentifierUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new PatientIdentifierDto
        {

        };
    }
}