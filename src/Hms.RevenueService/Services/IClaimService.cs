using Hms.RevenueService.Contracts;

namespace Hms.RevenueService.Services;

public interface IClaimService
{
    Task<ClaimDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ClaimDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ClaimDto> CreateAsync(CreateClaimRequest request, CancellationToken ct = default);
    Task<ClaimDto> UpdateAsync(UpdateClaimRequest request, CancellationToken ct = default);
}