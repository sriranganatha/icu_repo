using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class AgentContext
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string RequirementsBasePath { get; init; } = string.Empty;
    public string OutputBasePath { get; init; } = string.Empty;
    public List<Requirement> Requirements { get; set; } = [];
    public List<CodeArtifact> Artifacts { get; set; } = [];
    public List<ReviewFinding> Findings { get; set; } = [];
    public List<AgentMessage> Messages { get; set; } = [];
    public List<TestDiagnostic> TestDiagnostics { get; set; } = [];
    public Dictionary<AgentType, AgentStatus> AgentStatuses { get; set; } = [];
    public Dictionary<AgentType, int> RetryAttempts { get; set; } = [];
    public PipelineConfig? PipelineConfig { get; set; }
    public ParsedDomainModel? DomainModel { get; set; }
    public int ReviewIteration { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
