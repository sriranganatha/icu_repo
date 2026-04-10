using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// A resolved tech stack entry carrying the technology name and version
/// so agents can generate code for the right target.
/// </summary>
public sealed class ResolvedTechStackEntry
{
    public string TechnologyId { get; init; } = string.Empty;
    public string TechnologyName { get; init; } = string.Empty;
    public string TechnologyType { get; init; } = string.Empty; // language | framework | database | cloud | devops | package_registry | api_protocol
    public string Layer { get; init; } = string.Empty; // backend | frontend | database | infrastructure | testing
    public string Version { get; init; } = string.Empty;
    public string? ConfigOverridesJson { get; init; }
}

/// <summary>
/// Per-agent configuration override for a specific project.
/// Allows a project to use a different LLM model or system prompt for a given agent.
/// </summary>
public sealed class ProjectAgentConfig
{
    public AgentType AgentType { get; init; }
    public string? LlmModelId { get; init; }
    public string? SystemPromptOverride { get; init; }
    public double? TemperatureOverride { get; init; }
    public int? MaxTokensOverride { get; init; }
    public string? ConstraintsJson { get; init; }
}

/// <summary>
/// A resolved workflow stage with agent assignments
/// loaded from <c>StageDefinition</c> + <c>ApprovalGateConfig</c>.
/// </summary>
public sealed class ResolvedStage
{
    public string StageId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Order { get; init; }
    public List<AgentType> AgentsInvolved { get; init; } = [];
    public string? EntryCriteria { get; init; }
    public string? ExitCriteria { get; init; }
    public ResolvedApprovalGate? ApprovalGate { get; init; }
}

/// <summary>
/// Resolved approval gate configuration for a workflow stage.
/// </summary>
public sealed class ResolvedApprovalGate
{
    public string GateType { get; init; } = "auto"; // auto | human | hybrid
    public string? ApproversConfigJson { get; init; }
    public int TimeoutHours { get; init; } = 24;
}
