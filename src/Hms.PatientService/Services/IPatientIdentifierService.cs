using Hms.PatientService.Contracts;

namespace Hms.PatientService.Services;

public interface IPatientIdentifierService
{
    Task<PatientIdentifierDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<PatientIdentifierDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<PatientIdentifierDto> CreateAsync(CreatePatientIdentifierRequest request, CancellationToken ct = default);
    Task<PatientIdentifierDto> UpdateAsync(UpdatePatientIdentifierRequest request, CancellationToken ct = default);
}