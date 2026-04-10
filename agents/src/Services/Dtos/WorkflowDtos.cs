namespace GNex.Services.Dtos.Platform;

// ─── Workflow DTOs ──────────────────────────────────────────
public sealed record SdlcWorkflowDto(
    string Id, string Name, string Description, bool IsDefault, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    List<StageDefinitionDto> Stages);

public sealed record StageDefinitionDto(
    string Id, string WorkflowId, string Name, int Order,
    string? EntryCriteria, string? ExitCriteria, string AgentsInvolvedJson,
    List<ApprovalGateConfigDto> ApprovalGates,
    List<TransitionRuleDto> TransitionsFrom,
    List<TransitionRuleDto> TransitionsTo);

public sealed record ApprovalGateConfigDto(
    string Id, string StageId, string GateType,
    string? ApproversConfigJson, int TimeoutHours);

public sealed record TransitionRuleDto(
    string Id, string FromStageId, string ToStageId,
    string? ConditionsJson, bool AutoTransition);

// ─── Create / Update Requests ───────────────────────────────
public sealed record CreateWorkflowRequest(
    string Name, string Description, bool IsDefault);

public sealed record UpdateWorkflowRequest(
    string Id, string Name, string Description, bool IsDefault);

public sealed record CreateStageRequest(
    string WorkflowId, string Name, int Order,
    string? EntryCriteria, string? ExitCriteria, string AgentsInvolvedJson);

public sealed record UpdateStageRequest(
    string Id, string Name, int Order,
    string? EntryCriteria, string? ExitCriteria, string AgentsInvolvedJson);

public sealed record CreateApprovalGateRequest(
    string StageId, string GateType, string? ApproversConfigJson, int TimeoutHours);

public sealed record CreateTransitionRuleRequest(
    string FromStageId, string ToStageId, string? ConditionsJson, bool AutoTransition);
