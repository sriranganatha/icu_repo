using Hms.Services.Dtos.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hms.Services.Diagnostics;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(ILogger<DiagnosticsService> logger) => _logger = logger;

    public Task<ResultRecordDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting ResultRecord {Id}", id);
        // TODO: wire repository
        return Task.FromResult<ResultRecordDto?>(null);
    }

    public Task<List<ResultRecordDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<ResultRecordDto>());
    }

    public Task<ResultRecordDto> CreateAsync(CreateResultRecordRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating ResultRecord for facility {Facility}", request.FacilityId);
        var dto = new ResultRecordDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<ResultRecordDto> UpdateAsync(UpdateResultRecordRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating ResultRecord {Id}", request.Id);
        return Task.FromResult(new ResultRecordDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}