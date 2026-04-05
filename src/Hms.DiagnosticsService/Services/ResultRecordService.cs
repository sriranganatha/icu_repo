using Hms.DiagnosticsService.Contracts;
using Hms.DiagnosticsService.Data.Entities;
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

        };
    }

    public async Task<List<ResultRecordDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new ResultRecordDto
        {

        }).ToList();
    }

    public async Task<ResultRecordDto> CreateAsync(CreateResultRecordRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating ResultRecord for tenant {Tenant}", request.TenantId);

        var entity = new ResultRecord
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new ResultRecordCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created ResultRecord {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new ResultRecordDto
        {

        };
    }

    public async Task<ResultRecordDto> UpdateAsync(UpdateResultRecordRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating ResultRecord {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"ResultRecord {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new ResultRecordUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new ResultRecordDto
        {

        };
    }
}