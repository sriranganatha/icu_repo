using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Core.Interfaces;

public interface IAgent
{
    AgentType Type { get; }
    string Name { get; }
    string Description { get; }
    Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default);
}
