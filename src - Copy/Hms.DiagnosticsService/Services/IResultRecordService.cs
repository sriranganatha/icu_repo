using Hms.DiagnosticsService.Contracts;

namespace Hms.DiagnosticsService.Services;

public interface IResultRecordService
{
    Task<ResultRecordDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<ResultRecordDto>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task<ResultRecordDto> CreateAsync(CreateResultRecordRequest request, CancellationToken ct = default);
    Task<ResultRecordDto> UpdateAsync(UpdateResultRecordRequest request, CancellationToken ct = default);
}