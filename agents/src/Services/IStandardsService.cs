using Hms.Services.Dtos.Platform;

namespace Hms.Services.Platform;

public interface IStandardsService
{
    // Coding Standards
    Task<CodingStandardDto?> GetCodingStandardAsync(string id, CancellationToken ct = default);
    Task<List<CodingStandardDto>> ListCodingStandardsAsync(string? languageId = null, CancellationToken ct = default);
    Task<CodingStandardDto> CreateCodingStandardAsync(CreateCodingStandardRequest request, CancellationToken ct = default);
    Task<CodingStandardDto> UpdateCodingStandardAsync(UpdateCodingStandardRequest request, CancellationToken ct = default);
    Task DeleteCodingStandardAsync(string id, CancellationToken ct = default);

    // Naming Conventions
    Task<List<NamingConventionDto>> ListNamingConventionsAsync(CancellationToken ct = default);
    Task<NamingConventionDto> CreateNamingConventionAsync(CreateNamingConventionRequest request, CancellationToken ct = default);
    Task DeleteNamingConventionAsync(string id, CancellationToken ct = default);

    // Quality Gates
    Task<QualityGateDto?> GetQualityGateAsync(string id, CancellationToken ct = default);
    Task<List<QualityGateDto>> ListQualityGatesAsync(CancellationToken ct = default);
    Task<QualityGateDto> CreateQualityGateAsync(CreateQualityGateRequest request, CancellationToken ct = default);
    Task<QualityGateDto> UpdateQualityGateAsync(UpdateQualityGateRequest request, CancellationToken ct = default);
    Task DeleteQualityGateAsync(string id, CancellationToken ct = default);

    // Review Checklists
    Task<List<ReviewChecklistDto>> ListReviewChecklistsAsync(string? scope = null, CancellationToken ct = default);
    Task<ReviewChecklistDto> CreateReviewChecklistAsync(CreateReviewChecklistRequest request, CancellationToken ct = default);
    Task DeleteReviewChecklistAsync(string id, CancellationToken ct = default);

    // Security Policies
    Task<SecurityPolicyDto?> GetSecurityPolicyAsync(string id, CancellationToken ct = default);
    Task<List<SecurityPolicyDto>> ListSecurityPoliciesAsync(string? category = null, CancellationToken ct = default);
    Task<SecurityPolicyDto> CreateSecurityPolicyAsync(CreateSecurityPolicyRequest request, CancellationToken ct = default);
    Task<SecurityPolicyDto> UpdateSecurityPolicyAsync(UpdateSecurityPolicyRequest request, CancellationToken ct = default);
    Task DeleteSecurityPolicyAsync(string id, CancellationToken ct = default);
}
