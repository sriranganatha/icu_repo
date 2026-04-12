using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// A structured implementation plan created by the <c>PlanningAgent</c>.
/// Code-gen agents read this plan to understand:
///   - What services and entities they need to generate
///   - What order to follow
///   - What cross-cutting concerns apply
///   - What standards must be met
///   - What specific instructions each agent should follow
/// </summary>
public sealed class ImplementationPlan
{
    public string RunId { get; init; } = string.Empty;
    public int Iteration { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Services that need implementation or updates in this iteration.</summary>
    public List<ServicePlan> AffectedServices { get; set; } = [];

    /// <summary>Ordered steps for implementation — respects inter-service and inter-layer dependencies.</summary>
    public List<ExecutionStep> ExecutionOrder { get; set; } = [];

    /// <summary>Cross-cutting concerns that apply across all services (security, compliance, observability).</summary>
    public List<CrossCuttingConcern> CrossCuttingConcerns { get; set; } = [];

    /// <summary>Per-agent instructions: AgentType → detailed plan text.</summary>
    public Dictionary<AgentType, string> AgentInstructions { get; set; } = [];

    /// <summary>Standards and regulatory requirements that generated code must meet.</summary>
    public List<string> Standards { get; set; } = [];

    /// <summary>LLM-generated review notes identifying risks, gaps, and recommendations.</summary>
    public string? LlmReviewNotes { get; set; }
}

/// <summary>Per-service context gathered during planning.</summary>
public sealed class ServicePlan
{
    public string ServiceName { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public int Port { get; init; }
    public List<string> Entities { get; init; } = [];
    public List<string> DependsOn { get; init; } = [];
    public List<string> PublishedEvents { get; init; } = [];
    public List<string> ConsumedEvents { get; init; } = [];
    public bool HasActiveWork { get; set; }

    /// <summary>Entity → schema facts (from ContextBroker queries to Database agent).</summary>
    public Dictionary<string, Dictionary<string, string>> EntitySchemas { get; set; } = [];

    /// <summary>API contract facts (from ContextBroker queries to ServiceLayer agent).</summary>
    public Dictionary<string, string> ApiContracts { get; set; } = [];

    /// <summary>Integration contract facts (events, topics, consumer groups).</summary>
    public Dictionary<string, string> IntegrationContracts { get; set; } = [];

    /// <summary>Compliance constraints (regulatory, SOC2, audit).</summary>
    public Dictionary<string, string> ComplianceConstraints { get; set; } = [];
}

/// <summary>A single step in the implementation execution order.</summary>
public sealed class ExecutionStep
{
    public string ServiceName { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public AgentType AgentType { get; init; }
    public List<string> DependsOnSteps { get; init; } = [];
}

/// <summary>A cross-cutting concern that affects multiple layers/services.</summary>
public sealed class CrossCuttingConcern
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> AffectsLayers { get; init; } = [];
    public List<AgentType> EnforcedBy { get; init; } = [];
    public int Priority { get; init; }
}
