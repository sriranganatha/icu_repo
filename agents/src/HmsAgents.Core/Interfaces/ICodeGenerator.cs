using HmsAgents.Core.Models;

namespace HmsAgents.Core.Interfaces;

public interface ICodeGenerator
{
    Task<List<CodeArtifact>> GenerateAsync(
        List<Requirement> requirements,
        AgentContext context,
        CancellationToken ct = default);
}
