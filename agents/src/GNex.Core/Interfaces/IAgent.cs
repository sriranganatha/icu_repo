using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Core.Interfaces;

public interface IAgent
{
    AgentType Type { get; }
    string Name { get; }
    string Description { get; }
    Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default);
}
