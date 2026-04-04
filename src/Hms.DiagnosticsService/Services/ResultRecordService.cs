using Hms.DiagnosticsService.Contracts;
using Hms.DiagnosticsService.Data.Repositories;
using Hms.DiagnosticsService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.DiagnosticsService.Services;

public sealed class ResultRecordService : IResultRecordService
{
    private readonly IResultRecordRepository _repo;
    private readonly DiagnosticsServiceEventProducer _events;
    private readonly ILogger<ResultRecordService> _logger;

    public ResultRecordService(
        IResultRecordRepository repo,
        DiagnosticsServiceEventProducer events,
        ILogger<ResultRecordService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<ResultRecordDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new ResultRecordDto
        {
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<ResultRecordDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new ResultRecordDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<ResultRecordDto> CreateAsync(CreateResultRecordRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating ResultRecord for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new ResultRecordDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new ResultRecordCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<ResultRecordDto> UpdateAsync(UpdateResultRecordRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating ResultRecord {Id}", request.Id);
        await _events.PublishAsync(new ResultRecordUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new ResultRecordDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}