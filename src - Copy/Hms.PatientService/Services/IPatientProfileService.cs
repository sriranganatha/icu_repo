using Hms.PatientService.Contracts;

namespace Hms.PatientService.Services;

public interface IPatientProfileService
{
    Task<PatientProfileDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<PatientProfileDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<PatientProfileDto> CreateAsync(CreatePatientProfileRequest request, CancellationToken ct = default);
    Task<PatientProfileDto> UpdateAsync(UpdatePatientProfileRequest request, CancellationToken ct = default);
}