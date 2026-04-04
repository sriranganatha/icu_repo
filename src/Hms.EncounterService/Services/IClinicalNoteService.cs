using Hms.EncounterService.Contracts;

namespace Hms.EncounterService.Services;

public interface IClinicalNoteService
{
    Task<ClinicalNoteDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ClinicalNoteDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ClinicalNoteDto> CreateAsync(CreateClinicalNoteRequest request, CancellationToken ct = default);
    Task<ClinicalNoteDto> UpdateAsync(UpdateClinicalNoteRequest request, CancellationToken ct = default);
}