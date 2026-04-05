using Hms.EmergencyService.Contracts;

namespace Hms.EmergencyService.Services;

public interface ITriageAssessmentService
{
    Task<TriageAssessmentDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<TriageAssessmentDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<TriageAssessmentDto> CreateAsync(CreateTriageAssessmentRequest request, CancellationToken ct = default);
    Task<TriageAssessmentDto> UpdateAsync(UpdateTriageAssessmentRequest request, CancellationToken ct = default);
}