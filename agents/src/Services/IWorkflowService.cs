using GNex.Services.Dtos.Platform;

namespace GNex.Services.Platform;

public interface IWorkflowService
{
    Task<SdlcWorkflowDto?> GetWorkflowAsync(string id, CancellationToken ct = default);
    Task<List<SdlcWorkflowDto>> ListWorkflowsAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<SdlcWorkflowDto> CreateWorkflowAsync(CreateWorkflowRequest request, CancellationToken ct = default);
    Task<SdlcWorkflowDto> UpdateWorkflowAsync(UpdateWorkflowRequest request, CancellationToken ct = default);
    Task DeleteWorkflowAsync(string id, CancellationToken ct = default);

    // Stages
    Task<StageDefinitionDto> AddStageAsync(CreateStageRequest request, CancellationToken ct = default);
    Task<StageDefinitionDto> UpdateStageAsync(UpdateStageRequest request, CancellationToken ct = default);
    Task DeleteStageAsync(string id, CancellationToken ct = default);

    // Approval Gates
    Task<ApprovalGateConfigDto> AddApprovalGateAsync(CreateApprovalGateRequest request, CancellationToken ct = default);
    Task DeleteApprovalGateAsync(string id, CancellationToken ct = default);

    // Transition Rules
    Task<TransitionRuleDto> AddTransitionRuleAsync(CreateTransitionRuleRequest request, CancellationToken ct = default);
    Task DeleteTransitionRuleAsync(string id, CancellationToken ct = default);
}
