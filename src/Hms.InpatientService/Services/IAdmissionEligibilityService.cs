using Hms.InpatientService.Contracts;

namespace Hms.InpatientService.Services;

public interface IAdmissionEligibilityService
{
    Task<AdmissionEligibilityDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AdmissionEligibilityDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AdmissionEligibilityDto> CreateAsync(CreateAdmissionEligibilityRequest request, CancellationToken ct = default);
    Task<AdmissionEligibilityDto> UpdateAsync(UpdateAdmissionEligibilityRequest request, CancellationToken ct = default);
}