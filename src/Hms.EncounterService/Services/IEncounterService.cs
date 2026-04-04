using Hms.EncounterService.Contracts;

namespace Hms.EncounterService.Services;

public interface IEncounterService
{
    Task<EncounterDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<EncounterDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<EncounterDto> CreateAsync(CreateEncounterRequest request, CancellationToken ct = default);
    Task<EncounterDto> UpdateAsync(UpdateEncounterRequest request, CancellationToken ct = default);
}