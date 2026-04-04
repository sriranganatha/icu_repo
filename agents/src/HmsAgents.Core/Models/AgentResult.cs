using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class AgentResult
{
    public AgentType Agent { get; init; }
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<CodeArtifact> Artifacts { get; init; } = [];
    public List<ReviewFinding> Findings { get; init; } = [];
    public List<AgentMessage> Messages { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public List<TestDiagnostic> TestDiagnostics { get; init; } = [];
    public TimeSpan Duration { get; init; }
}
