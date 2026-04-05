namespace HmsAgents.Core.Models;

public sealed class PipelineConfig
{
    public string RequirementsPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string SolutionNamespace { get; init; } = "Hms";
    public string DbConnectionString { get; init; } = string.Empty;
    public bool EnableIntegrationLayer { get; init; } = true;
    public bool EnableTestGeneration { get; init; } = true;
    public bool EnableReviewAgent { get; init; } = true;
    public List<string> TargetModules { get; init; } = [];

    // Docker & database provisioning
    public string DockerContainerName { get; init; } = "icu-postgres";
    public string DbHost { get; init; } = "localhost";
    public int DbPort { get; init; } = 5432;
    public string DbName { get; init; } = "hms";
    public string DbPassword { get; init; } = "hms_dev_pw";
    public string DbUser { get; init; } = "hms";
    public bool SpinUpDocker { get; init; } = true;
    public bool ExecuteDdl { get; set; } = true;

    // Orchestrator instructions — user-provided directives to guide the pipeline
    public string OrchestratorInstructions { get; init; } = string.Empty;

    // Service port mapping
    public ServicePortMap ServicePorts { get; init; } = new();
}

public sealed class ServicePortMap
{
    public int Patient { get; init; } = 5101;
    public int Encounter { get; init; } = 5102;
    public int Inpatient { get; init; } = 5103;
    public int Emergency { get; init; } = 5104;
    public int Diagnostics { get; init; } = 5105;
    public int Revenue { get; init; } = 5106;
    public int Audit { get; init; } = 5107;
    public int Ai { get; init; } = 5108;
    public int Gateway { get; init; } = 5100;
    public int Kafka { get; init; } = 9092;
}
