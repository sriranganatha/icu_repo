namespace HmsAgents.Core.Models;

/// <summary>
/// Rich domain model extracted from requirements docs and entity artifacts.
/// Built by RequirementsReaderAgent, enriched from DatabaseAgent output.
/// Consumed by ServiceLayer, Testing, Review agents to generate complete code.
/// </summary>
public sealed class ParsedDomainModel
{
    public List<ParsedEntity> Entities { get; set; } = [];
    public List<FeatureMapping> FeatureMappings { get; set; } = [];
    public List<ParsedApiEndpoint> ApiEndpoints { get; set; } = [];
    public List<ParsedDomainEvent> DomainEvents { get; set; } = [];
    public List<NfrRequirement> NfrRequirements { get; set; } = [];
}

/// <summary>
/// A domain entity with its fields, extracted from DatabaseAgent's generated entity source.
/// </summary>
public sealed class ParsedEntity
{
    public string Name { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string ServiceShortName { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    public string DbContextName { get; init; } = string.Empty;
    public List<EntityField> Fields { get; init; } = [];
    public List<string> NavigationProperties { get; init; } = [];
    public List<string> FeatureTags { get; init; } = [];
}

/// <summary>
/// A single property on an entity, parsed from C# source.
/// </summary>
public sealed record EntityField
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsKey { get; init; }
    public bool IsNullable { get; init; }
    public string? DefaultValue { get; init; }
    public bool IsAuditField { get; init; }
    public bool IsNavigation { get; init; }
}

/// <summary>
/// Maps a feature/epic from the requirements to the implementing services and entities.
/// </summary>
public sealed record FeatureMapping
{
    public string FeatureId { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string FeatureName { get; init; } = string.Empty;
    public List<string> ServiceNames { get; init; } = [];
    public List<string> EntityNames { get; init; } = [];
}

/// <summary>
/// An API endpoint specification extracted from requirements.
/// </summary>
public sealed record ParsedApiEndpoint
{
    public string ServiceName { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
}

/// <summary>
/// A domain event specification.
/// </summary>
public sealed record ParsedDomainEvent
{
    public string ServiceName { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
}

/// <summary>
/// A non-functional requirement that must be validated by ReviewAgent.
/// </summary>
public sealed record NfrRequirement
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ValidationRule { get; init; } = string.Empty;
}
