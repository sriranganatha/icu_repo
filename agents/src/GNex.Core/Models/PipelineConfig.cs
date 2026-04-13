namespace GNex.Core.Models;

public sealed class PipelineConfig
{
    public string RequirementsPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string SolutionNamespace { get; init; } = "GNex";

    /// <summary>
    /// The project domain (e.g. "Healthcare", "IT", "Manufacturing", "E-Commerce", "FinTech", "Education").
    /// Used by agents to generate domain-appropriate artifacts, prompts, and compliance policies.
    /// When empty, agents generate fully generic artifacts.
    /// </summary>
    public string ProjectDomain { get; init; } = string.Empty;

    /// <summary>
    /// Optional free-text description of the domain to give agents richer context.
    /// Example: "Hospital management system with HL7/FHIR interoperability" or "B2B SaaS inventory platform".
    /// </summary>
    public string ProjectDomainDescription { get; init; } = string.Empty;
    public string DbConnectionString { get; init; } = string.Empty;
    public bool EnableIntegrationLayer { get; init; } = true;
    public bool EnableTestGeneration { get; init; } = true;
    public bool EnableReviewAgent { get; init; } = true;
    public List<string> TargetModules { get; init; } = [];

    // Docker & database provisioning
    public string DockerContainerName { get; init; } = "GNex-postgres";
    public string DbHost { get; init; } = "localhost";
    public int DbPort { get; init; } = 5418;
    public string DbName { get; init; } = "gnex_db";
    public string DbPassword { get; init; } = "GNex@1234";
    public string DbUser { get; init; } = "gnex_admin";
    public bool SpinUpDocker { get; init; } = true;
    public bool ExecuteDdl { get; set; } = true;

    // WIP limits — cap the number of items in Queue (Received) and InDev (InProgress) globally
    public int MaxQueueItems { get; init; } = 10;
    public int MaxInDevItems { get; init; } = 10;

    // Orchestrator instructions — user-provided directives to guide the pipeline
    public string OrchestratorInstructions { get; init; } = string.Empty;

    // Service port mapping — dynamic, populated from DerivedServices or user overrides
    public Dictionary<string, int> ServicePorts { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gateway"] = 5100,
        ["Kafka"] = 9092
    };

    // ── Convenience helpers for agents ──

    /// <summary>Short lowercase prefix for metrics, Kafka topics, file names (e.g. "app", "hms", "fintech").</summary>
    public string ProjectPrefix => string.IsNullOrWhiteSpace(ProjectDomain) ? "app" : ProjectDomain.ToLowerInvariant().Replace(" ", "").Replace("/", "");

    /// <summary>Human-readable project label for prompts and generated comments.</summary>
    public string ProjectLabel => string.IsNullOrWhiteSpace(ProjectDomain) ? "Application Platform" : $"{ProjectDomain} Platform";

    /// <summary>Domain context string agents can inject into LLM system prompts.</summary>
    public string DomainContext => string.IsNullOrWhiteSpace(ProjectDomainDescription)
        ? (string.IsNullOrWhiteSpace(ProjectDomain) ? "a generic software platform" : $"a {ProjectDomain} software platform")
        : ProjectDomainDescription;
}
