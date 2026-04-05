using Hms.InpatientService.Contracts;

namespace Hms.InpatientService.Services;

public interface IAdmissionService
{
    Task<AdmissionDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AdmissionDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AdmissionDto> CreateAsync(CreateAdmissionRequest request, CancellationToken ct = default);
    Task<AdmissionDto> UpdateAsync(UpdateAdmissionRequest request, CancellationToken ct = default);
}