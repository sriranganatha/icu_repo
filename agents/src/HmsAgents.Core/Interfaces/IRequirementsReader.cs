using HmsAgents.Core.Models;

namespace HmsAgents.Core.Interfaces;

public interface IRequirementsReader
{
    Task<List<Requirement>> ReadAllAsync(string basePath, CancellationToken ct = default);
    Task<List<Requirement>> ReadFileAsync(string filePath, CancellationToken ct = default);
}
