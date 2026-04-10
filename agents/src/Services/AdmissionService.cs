using GNex.Services.Dtos.Inpatient;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Inpatient;

public sealed class AdmissionService : IAdmissionService
{
    private readonly ILogger<AdmissionService> _logger;

    public AdmissionService(ILogger<AdmissionService> logger) => _logger = logger;

    public Task<AdmissionDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting Admission {Id}", id);
        // TODO: wire repository
        return Task.FromResult<AdmissionDto?>(null);
    }

    public Task<List<AdmissionDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AdmissionDto>());
    }

    public Task<AdmissionDto> CreateAsync(CreateAdmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Admission for facility {Facility}", request.FacilityId);
        var dto = new AdmissionDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<AdmissionDto> UpdateAsync(UpdateAdmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating Admission {Id}", request.Id);
        return Task.FromResult(new AdmissionDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}