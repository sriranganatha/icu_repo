using GNex.Core.Enums;

namespace GNex.Core.Models;

public sealed class AgentDirective
{
    public AgentType From { get; init; }
    public AgentType To { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public int Priority { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
