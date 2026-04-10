using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Core.Interfaces;

/// <summary>
/// Dynamically resolves <see cref="IAgent"/> instances based on DB-backed
/// <c>AgentTypeDefinition</c> records and per-project configuration overrides.
/// Replaces the hardcoded singleton agent registrations for project-scoped pipelines.
/// </summary>
public interface IAgentResolver
{
    /// <summary>
    /// Resolves the best agent for a given <see cref="AgentType"/> taking into account
    /// the project context (LLM model overrides, prompt overrides, etc.).
    /// Falls back to the DI-registered singleton if no DB-backed definition exists.
    /// </summary>
    Task<IAgent?> ResolveAsync(AgentType agentType, AgentContext context, CancellationToken ct = default);

    /// <summary>
    /// Given a task description, finds the best available agent based on capability matching
    /// from <c>AgentTypeDefinition.CapabilitiesJson</c>.
    /// </summary>
    Task<IAgent?> ResolveByCapabilityAsync(string capability, AgentContext context, CancellationToken ct = default);

    /// <summary>
    /// Returns all agents mapped for a project (from <c>AgentAssignment</c> table)
    /// or falls back to the DI-registered agents if no assignments exist.
    /// </summary>
    Task<IReadOnlyList<IAgent>> ResolveAllForProjectAsync(AgentContext context, CancellationToken ct = default);
}
