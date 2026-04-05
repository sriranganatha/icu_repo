using Hms.AiService.Contracts;
using Hms.AiService.Data.Entities;
using Hms.AiService.Data.Repositories;
using Hms.AiService.Kafka;
using Microsoft.Extensions.Logging;

namespace Hms.AiService.Services;

public sealed class AiInteractionService : IAiInteractionService
{
    private readonly IAiInteractionRepository _repo;
    private readonly AiServiceEventProducer _events;
    private readonly ILogger<AiInteractionService> _logger;

    public AiInteractionService(
        IAiInteractionRepository repo,
        AiServiceEventProducer events,
        ILogger<AiInteractionService> logger)
    {
        _repo = repo;
        _events = events;
        _logger = logger;
    }

    public async Task<AiInteractionDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        return new AiInteractionDto
        {

        };
    }

    public async Task<List<AiInteractionDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(entity => new AiInteractionDto
        {

        }).ToList();
    }

    public async Task<AiInteractionDto> CreateAsync(CreateAiInteractionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AiInteraction for tenant {Tenant}", request.TenantId);

        var entity = new AiInteraction
        {
            Id = Guid.NewGuid().ToString("N"),

            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var saved = await _repo.CreateAsync(entity, ct);

        await _events.PublishAsync(new AiInteractionCreatedEvent
        {
            EntityId = saved.Id, TenantId = saved.TenantId
        }, ct);

        _logger.LogInformation("Created AiInteraction {Id} for tenant {Tenant}", saved.Id, saved.TenantId);

        return new AiInteractionDto
        {

        };
    }

    public async Task<AiInteractionDto> UpdateAsync(UpdateAiInteractionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AiInteraction {Id}", request.Id);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"AiInteraction {request.Id} not found");


        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "system";

        await _repo.UpdateAsync(entity, ct);

        await _events.PublishAsync(new AiInteractionUpdatedEvent
        {
            EntityId = entity.Id, TenantId = entity.TenantId
        }, ct);

        return new AiInteractionDto
        {

        };
    }
}