using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// Defines a bounded-context microservice with its own database schema,
/// entities, API surface, and Docker-compose service entry.
/// Properties are mutable so the ArchitectAgent can construct them dynamically via LLM.
/// </summary>
public sealed class MicroserviceDefinition
{
    public required string Name { get; set; }
    public required string ShortName { get; set; }
    public required string Schema { get; set; }
    public required string Description { get; set; }
    public required int ApiPort { get; set; }
    public required string[] Entities { get; set; }
    public required string[] DependsOn { get; set; }
    public string Namespace => $"GNex.{Name}";
    public string ProjectName => $"GNex.{Name}";
    public string DbContextName => $"{Name}DbContext";
}

/// <summary>
/// Resolves the active service catalog for a pipeline run.
/// Prefers LLM-derived services from <see cref="AgentContext.DerivedServices"/>
/// and falls back to <see cref="MicroserviceCatalog.All"/> when none have been derived yet.
/// All agents should use this instead of referencing MicroserviceCatalog directly.
/// </summary>
public static class ServiceCatalogResolver
{
    public static IReadOnlyList<MicroserviceDefinition> GetServices(AgentContext context)
        => context.DerivedServices.Count > 0
            ? context.DerivedServices
            : MicroserviceCatalog.All;

    public static MicroserviceDefinition? ByName(AgentContext context, string name)
        => GetServices(context).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static MicroserviceDefinition? BySchema(AgentContext context, string schema)
        => GetServices(context).FirstOrDefault(s => s.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Legacy fallback service catalog — returns an empty array by default.
/// New pipelines rely on ArchitectAgent to derive services dynamically from
/// requirements via LLM. This catalog exists only for backward compatibility
/// with code paths that have not yet adopted ServiceCatalogResolver.
/// </summary>
public static class MicroserviceCatalog
{
    public static readonly MicroserviceDefinition[] All = [];

    public static MicroserviceDefinition? ByName(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static MicroserviceDefinition? BySchema(string schema) =>
        All.FirstOrDefault(s => s.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase));
}
