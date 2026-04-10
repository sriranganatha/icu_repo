using GNex.Database;
using GNex.Database.Entities.Platform.Workflows;
using GNex.Database.Repositories;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

public sealed class WorkflowService : IWorkflowService
{
    private readonly IPlatformRepository<SdlcWorkflow> _workflowRepo;
    private readonly GNexDbContext _db;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IPlatformRepository<SdlcWorkflow> workflowRepo,
        GNexDbContext db,
        ILogger<WorkflowService> logger)
    {
        _workflowRepo = workflowRepo;
        _db = db;
        _logger = logger;
    }

    public async Task<SdlcWorkflowDto?> GetWorkflowAsync(string id, CancellationToken ct = default)
    {
        var w = await _db.SdlcWorkflows
            .Include(x => x.Stages).ThenInclude(s => s.ApprovalGates)
            .Include(x => x.Stages).ThenInclude(s => s.TransitionsFrom)
            .Include(x => x.Stages).ThenInclude(s => s.TransitionsTo)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return w is null ? null : MapWorkflow(w);
    }

    public async Task<List<SdlcWorkflowDto>> ListWorkflowsAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var workflows = await _db.SdlcWorkflows
            .Where(x => x.IsActive)
            .Include(x => x.Stages)
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        return workflows.Select(w => MapWorkflow(w)).ToList();
    }

    public async Task<SdlcWorkflowDto> CreateWorkflowAsync(CreateWorkflowRequest request, CancellationToken ct = default)
    {
        var entity = new SdlcWorkflow
        {
            Name = request.Name,
            Description = request.Description,
            IsDefault = request.IsDefault
        };
        await _workflowRepo.CreateAsync(entity, ct);
        _logger.LogInformation("Created workflow {Id} '{Name}'", entity.Id, entity.Name);
        return MapWorkflow(entity);
    }

    public async Task<SdlcWorkflowDto> UpdateWorkflowAsync(UpdateWorkflowRequest request, CancellationToken ct = default)
    {
        var entity = await _workflowRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Workflow {request.Id} not found");
        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.IsDefault = request.IsDefault;
        await _workflowRepo.UpdateAsync(entity, ct);
        return MapWorkflow(entity);
    }

    public async Task DeleteWorkflowAsync(string id, CancellationToken ct = default)
        => await _workflowRepo.SoftDeleteAsync(id, ct);

    // ─── Stages ─────────────────────────────────────────────
    public async Task<StageDefinitionDto> AddStageAsync(CreateStageRequest request, CancellationToken ct = default)
    {
        var entity = new StageDefinition
        {
            WorkflowId = request.WorkflowId,
            Name = request.Name,
            Order = request.Order,
            EntryCriteria = request.EntryCriteria,
            ExitCriteria = request.ExitCriteria,
            AgentsInvolvedJson = request.AgentsInvolvedJson
        };
        _db.StageDefinitions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapStage(entity);
    }

    public async Task<StageDefinitionDto> UpdateStageAsync(UpdateStageRequest request, CancellationToken ct = default)
    {
        var entity = await _db.StageDefinitions.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"Stage {request.Id} not found");
        entity.Name = request.Name;
        entity.Order = request.Order;
        entity.EntryCriteria = request.EntryCriteria;
        entity.ExitCriteria = request.ExitCriteria;
        entity.AgentsInvolvedJson = request.AgentsInvolvedJson;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapStage(entity);
    }

    public async Task DeleteStageAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.StageDefinitions.FindAsync([id], ct);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    // ─── Approval Gates ─────────────────────────────────────
    public async Task<ApprovalGateConfigDto> AddApprovalGateAsync(CreateApprovalGateRequest request, CancellationToken ct = default)
    {
        var entity = new ApprovalGateConfig
        {
            StageId = request.StageId,
            GateType = request.GateType,
            ApproversConfigJson = request.ApproversConfigJson,
            TimeoutHours = request.TimeoutHours
        };
        _db.ApprovalGateConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new ApprovalGateConfigDto(entity.Id, entity.StageId, entity.GateType,
            entity.ApproversConfigJson, entity.TimeoutHours);
    }

    public async Task DeleteApprovalGateAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.ApprovalGateConfigs.FindAsync([id], ct);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    // ─── Transition Rules ───────────────────────────────────
    public async Task<TransitionRuleDto> AddTransitionRuleAsync(CreateTransitionRuleRequest request, CancellationToken ct = default)
    {
        var entity = new TransitionRule
        {
            FromStageId = request.FromStageId,
            ToStageId = request.ToStageId,
            ConditionsJson = request.ConditionsJson,
            AutoTransition = request.AutoTransition
        };
        _db.TransitionRules.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new TransitionRuleDto(entity.Id, entity.FromStageId, entity.ToStageId,
            entity.ConditionsJson, entity.AutoTransition);
    }

    public async Task DeleteTransitionRuleAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.TransitionRules.FindAsync([id], ct);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    // ─── Mapping ────────────────────────────────────────────
    private static SdlcWorkflowDto MapWorkflow(SdlcWorkflow w) => new(
        w.Id, w.Name, w.Description, w.IsDefault, w.IsActive,
        w.CreatedAt, w.UpdatedAt,
        w.Stages.Select(MapStage).OrderBy(s => s.Order).ToList());

    private static StageDefinitionDto MapStage(StageDefinition s) => new(
        s.Id, s.WorkflowId, s.Name, s.Order,
        s.EntryCriteria, s.ExitCriteria, s.AgentsInvolvedJson,
        s.ApprovalGates.Select(g => new ApprovalGateConfigDto(
            g.Id, g.StageId, g.GateType, g.ApproversConfigJson, g.TimeoutHours)).ToList(),
        s.TransitionsFrom.Select(t => new TransitionRuleDto(
            t.Id, t.FromStageId, t.ToStageId, t.ConditionsJson, t.AutoTransition)).ToList(),
        s.TransitionsTo.Select(t => new TransitionRuleDto(
            t.Id, t.FromStageId, t.ToStageId, t.ConditionsJson, t.AutoTransition)).ToList());
}
