using Hms.EmergencyService.Contracts;
using Hms.EmergencyService.Data.Entities;
using Hms.EmergencyService.Data.Repositories;
using Hms.EmergencyService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.EmergencyService.Services;

public sealed class TriageAssessmentService : ITriageAssessmentService
{
    private readonly ITriageAssessmentRepository _repo;
    private readonly EmergencyServiceEventProducer _events;
    private readonly ILogger<TriageAssessmentService> _logger;

    public TriageAssessmentService(
        ITriageAssessmentRepository repo,
        EmergencyServiceEventProducer events,
        ILogger<TriageAssessmentService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<TriageAssessmentDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new TriageAssessmentDto
        {

        };
    }

    public async Task<List<TriageAssessmentDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new TriageAssessmentDto
        {

        }).ToList();
    }

    public async Task<TriageAssessmentDto> CreateAsync(CreateTriageAssessmentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating TriageAssessment for tenant {Tenant}", request.TenantId);

        var entity = new TriageAssessment
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new TriageAssessmentCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created TriageAssessment {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new TriageAssessmentDto
        {

        };
    }

    public async Task<TriageAssessmentDto> UpdateAsync(UpdateTriageAssessmentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating TriageAssessment {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"TriageAssessment {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new TriageAssessmentUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new TriageAssessmentDto
        {

        };
    }
}