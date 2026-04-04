using Hms.Services.Dtos.Revenue;

namespace Hms.Services.Revenue;

public interface IClaimService
{
    Task<ClaimDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ClaimDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ClaimDto> CreateAsync(CreateClaimRequest request, CancellationToken ct = default);
    Task<ClaimDto> UpdateAsync(UpdateClaimRequest request, CancellationToken ct = default);
}