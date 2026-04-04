using Hms.AiService.Contracts;
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
            Id = entity.Id, TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task<List<AiInteractionDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        var items = await _repo.ListAsync(skip, take, ct);
        return items.Select(e => new AiInteractionDto
        {
            Id = e.Id, TenantId = e.TenantId, CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task<AiInteractionDto> CreateAsync(CreateAiInteractionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AiInteraction for tenant {Tenant}", request.TenantId);
        // TODO: map request to entity and save via repository
        var dto = new AiInteractionDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Publish domain event to Kafka
        await _events.PublishAsync(new AiInteractionCreatedEvent
        {
            EntityId = dto.Id, TenantId = dto.TenantId
        }, ct);

        return dto;
    }

    public async Task<AiInteractionDto> UpdateAsync(UpdateAiInteractionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AiInteraction {Id}", request.Id);
        await _events.PublishAsync(new AiInteractionUpdatedEvent
        {
            EntityId = request.Id, TenantId = string.Empty
        }, ct);
        return new AiInteractionDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" };
    }
}