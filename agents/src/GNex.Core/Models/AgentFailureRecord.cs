using GNex.Core.Enums;

namespace GNex.Core.Models;

public sealed class AgentFailureRecord
{
    public AgentType FailedAgent { get; init; }
    public int Attempt { get; init; }
    public string Error { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Remediated { get; set; }
    /// <summary>Fully-qualified exception type (e.g. System.ArgumentException) for pattern analysis.</summary>
    public string ExceptionType { get; init; } = string.Empty;
    /// <summary>Stack trace captured at the point of failure — enables root-cause analysis.</summary>
    public string StackTrace { get; init; } = string.Empty;
    /// <summary>True when the error is non-recoverable (auth failure, config error) and retries should be skipped.</summary>
    public bool NonRecoverable { get; init; }
}
