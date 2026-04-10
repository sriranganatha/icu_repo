using GNex.Services.Dtos.Revenue;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Revenue;

public sealed class ClaimService : IClaimService
{
    private readonly ILogger<ClaimService> _logger;

    public ClaimService(ILogger<ClaimService> logger) => _logger = logger;

    public Task<ClaimDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting Claim {Id}", id);
        // TODO: wire repository
        return Task.FromResult<ClaimDto?>(null);
    }

    public Task<List<ClaimDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<ClaimDto>());
    }

    public Task<ClaimDto> CreateAsync(CreateClaimRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Claim for facility {Facility}", request.FacilityId);
        var dto = new ClaimDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<ClaimDto> UpdateAsync(UpdateClaimRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Claim {Id}", request.Id);
        return Task.FromResult(new ClaimDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}