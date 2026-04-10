using GNex.Core.Enums;

namespace GNex.Core.Models;

public sealed class AgentMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public AgentType From { get; init; }
    public AgentType To { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object> Metadata { get; init; } = [];
}
