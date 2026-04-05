using System.Collections.Concurrent;
using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class AgentContext
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string RequirementsBasePath { get; init; } = string.Empty;
    public string OutputBasePath { get; init; } = string.Empty;
    public List<Requirement> Requirements { get; set; } = [];
    public List<ExpandedRequirement> ExpandedRequirements { get; set; } = [];
    public ConcurrentBag<CodeArtifact> Artifacts { get; set; } = [];
    public ConcurrentBag<ReviewFinding> Findings { get; set; } = [];
    public ConcurrentBag<AgentMessage> Messages { get; set; } = [];
    public ConcurrentBag<TestDiagnostic> TestDiagnostics { get; set; } = [];
    public ConcurrentDictionary<AgentType, AgentStatus> AgentStatuses { get; set; } = new();
    public ConcurrentDictionary<AgentType, int> RetryAttempts { get; set; } = new();
    public ConcurrentBag<AgentFailureRecord> FailureRecords { get; set; } = [];
    public PipelineConfig? PipelineConfig { get; set; }
    public ParsedDomainModel? DomainModel { get; set; }
    public int ReviewIteration { get; set; }
    /// <summary>Live instruction queue — new directives can be pushed mid-pipeline.</summary>
    public List<string> OrchestratorInstructions { get; set; } = [];
    /// <summary>Inter-agent message queue — agents post directives for other agents.</summary>
    public ConcurrentQueue<AgentDirective> DirectiveQueue { get; } = new();
    /// <summary>Development iteration counter — incremented each time the Review→BugFix loop completes.</summary>
    public int DevIteration { get; set; }
    /// <summary>Progress callback — agents call this to emit real-time running commentary to the dashboard. Pass (AgentType, message).</summary>
    public Func<AgentType, string, Task>? ReportProgress { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Set to true after the first DDL approval in this run — prevents re-prompting on agent re-dispatch.</summary>
    public bool DdlApprovedForRun { get; set; }
}
