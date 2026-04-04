using Hms.EmergencyService.Contracts;
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
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<TriageAssessmentDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new TriageAssessmentDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<TriageAssessmentDto> CreateAsync(CreateTriageAssessmentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating TriageAssessment for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new TriageAssessmentDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new TriageAssessmentCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<TriageAssessmentDto> UpdateAsync(UpdateTriageAssessmentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating TriageAssessment {Id}", request.Id);
        await _events.PublishAsync(new TriageAssessmentUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new TriageAssessmentDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}