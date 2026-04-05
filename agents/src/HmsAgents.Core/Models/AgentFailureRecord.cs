using HmsAgents.Core.Enums;

namespace HmsAgents.Core.Models;

public sealed class AgentFailureRecord
{
    public AgentType FailedAgent { get; init; }
    public int Attempt { get; init; }
    public string Error { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Remediated { get; set; }
}
