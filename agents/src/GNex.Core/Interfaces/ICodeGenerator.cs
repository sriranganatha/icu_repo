using GNex.Core.Models;

namespace GNex.Core.Interfaces;

public interface ICodeGenerator
{
    Task<List<CodeArtifact>> GenerateAsync(
        List<Requirement> requirements,
        AgentContext context,
        CancellationToken ct = default);
}
