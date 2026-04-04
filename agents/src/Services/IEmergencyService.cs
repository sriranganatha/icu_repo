using Hms.Services.Dtos.Emergency;

namespace Hms.Services.Emergency;

public interface IEmergencyService
{
    Task<EmergencyArrivalDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<EmergencyArrivalDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<EmergencyArrivalDto> CreateAsync(CreateEmergencyArrivalRequest request, CancellationToken ct = default);
    Task<EmergencyArrivalDto> UpdateAsync(UpdateEmergencyArrivalRequest request, CancellationToken ct = default);
}