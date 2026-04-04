namespace HmsAgents.Core.Enums;

public enum AgentStatus
{
    Idle,
    Running,
    WaitingForDependency,
    Completed,
    Failed,
    ReviewPending
}
