using GNex.Core.Models;

namespace GNex.Core.Interfaces;

public interface IRequirementsReader
{
    Task<List<Requirement>> ReadAllAsync(string basePath, CancellationToken ct = default);
    Task<List<Requirement>> ReadFileAsync(string filePath, CancellationToken ct = default);
    Task<List<Requirement>> ReadFromBrdAsync(string projectId, CancellationToken ct = default);
}
