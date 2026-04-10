using GNex.Services.Dtos.Diagnostics;

namespace GNex.Services.Diagnostics;

public interface IDiagnosticsService
{
    Task<ResultRecordDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ResultRecordDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ResultRecordDto> CreateAsync(CreateResultRecordRequest request, CancellationToken ct = default);
    Task<ResultRecordDto> UpdateAsync(UpdateResultRecordRequest request, CancellationToken ct = default);
}