using HmsAgents.Core.Models;

namespace HmsAgents.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<AgentContext> RunPipelineAsync(PipelineConfig config, CancellationToken ct = default);
    Task<AgentContext> RunSingleAgentAsync(PipelineConfig config, Enums.AgentType agentType, CancellationToken ct = default);
    AgentContext? GetCurrentContext();
}
