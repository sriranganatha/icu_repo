using GNex.Services.Dtos.Mpi;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Mpi;

public sealed class PatientService : IPatientService
{
    private readonly ILogger<PatientService> _logger;

    public PatientService(ILogger<PatientService> logger) => _logger = logger;

    public Task<PatientProfileDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting PatientProfile {Id}", id);
        // TODO: wire repository
        return Task.FromResult<PatientProfileDto?>(null);
    }

    public Task<List<PatientProfileDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<PatientProfileDto>());
    }

    public Task<PatientProfileDto> CreateAsync(CreatePatientProfileRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating PatientProfile for facility {Facility}", request.FacilityId);
        var dto = new PatientProfileDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<PatientProfileDto> UpdateAsync(UpdatePatientProfileRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating PatientProfile {Id}", request.Id);
        return Task.FromResult(new PatientProfileDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}