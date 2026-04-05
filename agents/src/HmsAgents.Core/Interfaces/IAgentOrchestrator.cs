using HmsAgents.Core.Models;

namespace HmsAgents.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<AgentContext> RunPipelineAsync(PipelineConfig config, CancellationToken ct = default);
    Task<AgentContext> RunSingleAgentAsync(PipelineConfig config, Enums.AgentType agentType, CancellationToken ct = default);
    AgentContext? GetCurrentContext();
    /// <summary>
    /// Adds new requirements mid-pipeline: persists them to docs, re-runs RequirementsReader,
    /// triggers RequirementsExpander + Backlog, then dispatches affected agents.
    /// </summary>
    Task AddRequirementsAsync(List<Requirement> newRequirements, CancellationToken ct = default);
}
