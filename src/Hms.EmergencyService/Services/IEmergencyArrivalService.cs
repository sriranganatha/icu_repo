using Hms.EmergencyService.Contracts;

namespace Hms.EmergencyService.Services;

public interface IEmergencyArrivalService
{
    Task<EmergencyArrivalDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<EmergencyArrivalDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<EmergencyArrivalDto> CreateAsync(CreateEmergencyArrivalRequest request, CancellationToken ct = default);
    Task<EmergencyArrivalDto> UpdateAsync(UpdateEmergencyArrivalRequest request, CancellationToken ct = default);
}