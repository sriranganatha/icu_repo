using GNex.Services.Dtos.Governance;

namespace GNex.Services.Governance;

public interface IAuditService
{
    Task<AuditEventDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AuditEventDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AuditEventDto> CreateAsync(CreateAuditEventRequest request, CancellationToken ct = default);
    Task<AuditEventDto> UpdateAsync(UpdateAuditEventRequest request, CancellationToken ct = default);
}