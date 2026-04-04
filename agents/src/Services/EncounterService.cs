using Hms.Services.Dtos.Clinical;
using Microsoft.Extensions.Logging;

namespace Hms.Services.Clinical;

public sealed class EncounterService : IEncounterService
{
    private readonly ILogger<EncounterService> _logger;

    public EncounterService(ILogger<EncounterService> logger) => _logger = logger;

    public Task<EncounterDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting Encounter {Id}", id);
        // TODO: wire repository
        return Task.FromResult<EncounterDto?>(null);
    }

    public Task<List<EncounterDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<EncounterDto>());
    }

    public Task<EncounterDto> CreateAsync(CreateEncounterRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Encounter for facility {Facility}", request.FacilityId);
        var dto = new EncounterDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<EncounterDto> UpdateAsync(UpdateEncounterRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Encounter {Id}", request.Id);
        return Task.FromResult(new EncounterDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}