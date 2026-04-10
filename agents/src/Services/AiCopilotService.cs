using GNex.Services.Dtos.Ai;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Ai;

public sealed class AiCopilotService : IAiCopilotService
{
    private readonly ILogger<AiCopilotService> _logger;

    public AiCopilotService(ILogger<AiCopilotService> logger) => _logger = logger;

    public Task<AiInteractionDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting AiInteraction {Id}", id);
        // TODO: wire repository
        return Task.FromResult<AiInteractionDto?>(null);
    }

    public Task<List<AiInteractionDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AiInteractionDto>());
    }

    public Task<AiInteractionDto> CreateAsync(CreateAiInteractionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AiInteraction for facility {Facility}", request.FacilityId);
        var dto = new AiInteractionDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<AiInteractionDto> UpdateAsync(UpdateAiInteractionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AiInteraction {Id}", request.Id);
        return Task.FromResult(new AiInteractionDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}