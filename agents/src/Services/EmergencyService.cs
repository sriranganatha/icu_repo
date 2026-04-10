using GNex.Services.Dtos.Emergency;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Emergency;

public sealed class EmergencyService : IEmergencyService
{
    private readonly ILogger<EmergencyService> _logger;

    public EmergencyService(ILogger<EmergencyService> logger) => _logger = logger;

    public Task<EmergencyArrivalDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting EmergencyArrival {Id}", id);
        // TODO: wire repository
        return Task.FromResult<EmergencyArrivalDto?>(null);
    }

    public Task<List<EmergencyArrivalDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<EmergencyArrivalDto>());
    }

    public Task<EmergencyArrivalDto> CreateAsync(CreateEmergencyArrivalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating EmergencyArrival for facility {Facility}", request.FacilityId);
        var dto = new EmergencyArrivalDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<EmergencyArrivalDto> UpdateAsync(UpdateEmergencyArrivalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating EmergencyArrival {Id}", request.Id);
        return Task.FromResult(new EmergencyArrivalDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}