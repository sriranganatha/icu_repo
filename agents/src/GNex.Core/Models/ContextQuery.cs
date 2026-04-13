using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// A structured query one agent sends to another to request context or information.
/// This enables agents to "talk" to each other during code generation, asking for
/// domain knowledge, schema details, API contracts, or implementation decisions.
/// </summary>
public sealed class ContextQuery
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public AgentType From { get; init; }
    public AgentType To { get; init; }

    /// <summary>The category of information requested.</summary>
    public QueryIntent Intent { get; init; }

    /// <summary>Specific question or data request (e.g., "Entity fields", "Service API contract").</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>Module or service scope for the query.</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>Optional entity or artifact name to narrow the query.</summary>
    public string EntityName { get; init; } = string.Empty;

    /// <summary>When the query was raised.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The response to a <see cref="ContextQuery"/>. Contains structured data
/// that the requesting agent can use to make informed code generation decisions.
/// </summary>
public sealed class ContextResponse
{
    public string QueryId { get; init; } = string.Empty;
    public AgentType RespondedBy { get; init; }
    public bool Success { get; init; }

    /// <summary>Structured answer — typically a summary of schema, API, or design decisions.</summary>
    public string Answer { get; init; } = string.Empty;

    /// <summary>Code snippets relevant to the query (e.g., entity definitions, DTOs, interfaces).</summary>
    public List<string> CodeSnippets { get; init; } = [];

    /// <summary>Key-value facts (e.g., "PrimaryKey" → "EntityId", "TenantColumn" → "TenantId").</summary>
    public Dictionary<string, string> Facts { get; init; } = [];

    /// <summary>Related artifact IDs the requesting agent should reference.</summary>
    public List<string> RelatedArtifactIds { get; init; } = [];
}

/// <summary>Categories of information an agent can request from another.</summary>
public enum QueryIntent
{
    /// <summary>Request entity schema: fields, types, relationships, constraints.</summary>
    EntitySchema,

    /// <summary>Request API contract: endpoints, DTOs, validation rules.</summary>
    ApiContract,

    /// <summary>Request integration details: events, topics, consumer groups.</summary>
    IntegrationContract,

    /// <summary>Request security requirements: auth rules, data classification, sensitive fields.</summary>
    SecurityRequirements,

    /// <summary>Request domain rules: business logic, state machines, invariants.</summary>
    DomainRules,

    /// <summary>Request compliance constraints: regulatory, SOC2, audit requirements for an entity.</summary>
    ComplianceConstraints,

    /// <summary>Request current implementation status: what's built, what's missing.</summary>
    ImplementationStatus,

    /// <summary>Request architecture decisions: patterns, tech choices, rationale.</summary>
    ArchitectureDecision,

    /// <summary>Request dependency info: which services this entity/feature depends on.</summary>
    DependencyInfo,

    /// <summary>Request test strategy: what test types are needed, coverage expectations.</summary>
    TestStrategy
}
