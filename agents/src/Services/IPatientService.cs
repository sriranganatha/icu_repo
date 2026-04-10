using GNex.Services.Dtos.Mpi;

namespace GNex.Services.Mpi;

public interface IPatientService
{
    Task<PatientProfileDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<PatientProfileDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<PatientProfileDto> CreateAsync(CreatePatientProfileRequest request, CancellationToken ct = default);
    Task<PatientProfileDto> UpdateAsync(UpdatePatientProfileRequest request, CancellationToken ct = default);
}