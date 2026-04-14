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

    /// <summary>
    /// When true, all inter-agent communication (WriteFeedback, ReadFeedback, DispatchFindings,
    /// AgentResults) is logged to CommunicationLog on AgentContext and persisted to DB.
    /// Agents can read these logs for self-improvement and runtime analysis.
    /// </summary>
    public bool EnableAgentCommunicationLogging { get; init; } = true;

    /// <summary>
    /// When non-null, the pipeline is resuming from a prior interrupted run.
    /// Agents whose names appear in this set will be skipped (they already completed).
    /// </summary>
    public HashSet<string>? ResumeCompletedAgents { get; set; }

    /// <summary>
    /// Pre-loaded requirements from a prior completed RequirementsReader run.
    /// When resuming, these are injected into the context so downstream agents
    /// (RequirementsExpander, etc.) have data to work with.
    /// </summary>
    public List<Requirement>? ResumeRequirements { get; set; }

    /// <summary>Pre-loaded expanded requirements (backlog items) from a prior run for resume.</summary>
    public List<ExpandedRequirement>? ResumeExpandedRequirements { get; set; }

    /// <summary>Pre-loaded DerivedServices from a prior Architect run for resume.</summary>
    public List<MicroserviceDefinition>? ResumeDerivedServices { get; set; }

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

    /// <summary>
    /// Optional callback for incremental work-item persistence. When set, agents can call this
    /// to save expanded requirements to the DB mid-execution so data survives crashes.
    /// Parameters: (runId, projectId, items).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<string, string?, IReadOnlyList<ExpandedRequirement>>? PersistWorkItems { get; set; }
}
