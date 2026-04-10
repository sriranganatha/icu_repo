using GNex.Core.Models;

namespace GNex.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<AgentContext> RunPipelineAsync(PipelineConfig config, CancellationToken ct = default);

    /// <summary>
    /// Runs a project-scoped pipeline: resolves the project's tech stack, workflow,
    /// and agent assignments from DB, then executes the pipeline with isolated context.
    /// </summary>
    Task<AgentContext> RunProjectPipelineAsync(string projectId, PipelineConfig config, CancellationToken ct = default);

    Task<AgentContext> RunSingleAgentAsync(PipelineConfig config, Enums.AgentType agentType, CancellationToken ct = default);
    AgentContext? GetCurrentContext();

    /// <summary>
    /// Returns the <see cref="AgentContext"/> for a specific project pipeline run,
    /// or null if no pipeline is active for that project.
    /// </summary>
    AgentContext? GetProjectContext(string projectId);

    /// <summary>
    /// Returns all currently active pipeline contexts (for concurrent project support).
    /// </summary>
    IReadOnlyDictionary<string, AgentContext> GetActiveContexts();

    /// <summary>
    /// Adds new requirements mid-pipeline: persists them to docs, re-runs RequirementsReader,
    /// triggers RequirementsExpander + Backlog, then dispatches affected agents.
    /// </summary>
    Task AddRequirementsAsync(List<Requirement> newRequirements, CancellationToken ct = default);
    /// <summary>Clears the in-memory context so the next run starts completely fresh.</summary>
    void ResetContext();
}
