using GNex.Services.Dtos.Governance;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Governance;

public sealed class AuditService : IAuditService
{
    private readonly ILogger<AuditService> _logger;

    public AuditService(ILogger<AuditService> logger) => _logger = logger;

    public Task<AuditEventDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting AuditEvent {Id}", id);
        // TODO: wire repository
        return Task.FromResult<AuditEventDto?>(null);
    }

    public Task<List<AuditEventDto>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AuditEventDto>());
    }

    public Task<AuditEventDto> CreateAsync(CreateAuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating AuditEvent for facility {Facility}", request.FacilityId);
        var dto = new AuditEventDto
        {
            Id = Guid.NewGuid().ToString("N"),
            FacilityId = request.FacilityId,
            StatusCode = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Task.FromResult(dto);
    }

    public Task<AuditEventDto> UpdateAsync(UpdateAuditEventRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating AuditEvent {Id}", request.Id);
        return Task.FromResult(new AuditEventDto { Id = request.Id, StatusCode = request.StatusCode ?? "active" });
    }
}