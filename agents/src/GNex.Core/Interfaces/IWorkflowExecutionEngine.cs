using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Core.Interfaces;

/// <summary>
/// Loads SDLC workflow definitions from DB and drives stage-based execution.
/// Replaces the hardcoded <c>s_dependencies</c> DAG when a project has a DB-backed workflow.
/// </summary>
public interface IWorkflowExecutionEngine
{
    /// <summary>
    /// Loads the workflow for a project (or the default workflow) and resolves all stages
    /// into ordered <see cref="ResolvedStage"/> entries on the context.
    /// </summary>
    Task LoadWorkflowAsync(AgentContext context, string? workflowId, CancellationToken ct = default);

    /// <summary>
    /// Given a set of completed agent types, returns the next set of agents that are ready
    /// to execute based on stage ordering and entry criteria.
    /// </summary>
    IReadOnlyList<AgentType> GetReadyAgents(AgentContext context, IReadOnlySet<AgentType> completedAgents);

    /// <summary>
    /// Checks whether the exit criteria for a stage are satisfied.
    /// Returns true if the stage is complete and the pipeline can advance.
    /// </summary>
    bool IsStageComplete(AgentContext context, ResolvedStage stage, IReadOnlySet<AgentType> completedAgents);

    /// <summary>
    /// Checks whether an approval gate blocks progression past a stage.
    /// Returns true if the gate requires human approval and hasn't been approved yet.
    /// </summary>
    Task<bool> IsApprovalRequiredAsync(AgentContext context, ResolvedStage stage, CancellationToken ct = default);

    /// <summary>
    /// Records that a human has approved the gate for a stage in this run.
    /// </summary>
    Task ApproveGateAsync(AgentContext context, string stageId, string approver, CancellationToken ct = default);
}
