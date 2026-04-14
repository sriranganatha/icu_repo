using GNex.Core.Enums;

namespace GNex.Core.Models;

/// <summary>
/// Records a single inter-agent communication event (WriteFeedback, ReadFeedback,
/// DispatchFindings, AgentResults storage). Used for runtime analysis and self-improvement.
/// </summary>
public sealed class AgentCommunicationEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..16];
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string RunId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }

    /// <summary>The type of communication event.</summary>
    public AgentCommType CommType { get; init; }

    /// <summary>The agent initiating the communication.</summary>
    public AgentType FromAgent { get; init; }

    /// <summary>The target agent (if applicable).</summary>
    public AgentType? ToAgent { get; init; }

    /// <summary>The message or summary of the communication.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Number of items involved (e.g. feedback count, findings count).</summary>
    public int ItemCount { get; init; }

    /// <summary>Optional category (e.g. finding category for DispatchFindings).</summary>
    public string? Category { get; init; }
}

/// <summary>Types of inter-agent communication events.</summary>
public enum AgentCommType
{
    WriteFeedback,
    ReadFeedback,
    DispatchFindings,
    StoreResult,
    ReadResult,
    DirectiveQueued,
    DirectiveProcessed
}
