using GNex.Core.Models;

namespace GNex.Core.Interfaces;

/// <summary>
/// Mediates inter-agent context queries. Code-gen agents call
/// <see cref="ResolveAsync"/> to get structured answers from the shared context
/// without needing direct references to other agents.
/// </summary>
public interface IContextBroker
{
    /// <summary>
    /// Resolve a context query by inspecting existing artifacts, domain model,
    /// requirements, and agent outputs in the shared context.
    /// </summary>
    Task<ContextResponse> ResolveAsync(ContextQuery query, AgentContext context, CancellationToken ct = default);
}
