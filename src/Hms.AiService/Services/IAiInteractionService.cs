using Hms.AiService.Contracts;

namespace Hms.AiService.Services;

public interface IAiInteractionService
{
    Task<AiInteractionDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<AiInteractionDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<AiInteractionDto> CreateAsync(CreateAiInteractionRequest request, CancellationToken ct = default);
    Task<AiInteractionDto> UpdateAsync(UpdateAiInteractionRequest request, CancellationToken ct = default);
}