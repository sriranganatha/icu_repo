using GNex.Core.Interfaces;
using GNex.Core.Models;
using GNex.Database;
using GNex.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

/// <summary>Broadcasts BRD status changes to connected clients.</summary>
public interface IBrdStatusNotifier
{
    Task NotifyBrdStatusChangedAsync(string projectId, string status, string message, CancellationToken ct = default);
}

public interface IBrdWorkflowService
{
    Task<BrdWorkflowResult> SubmitForReviewAsync(string projectId, string reviewer, CancellationToken ct = default);
    Task<BrdWorkflowResult> ApproveAsync(string projectId, string reviewer, string? comment = null, CancellationToken ct = default);
    Task<BrdWorkflowResult> RejectAsync(string projectId, string reviewer, string reason, CancellationToken ct = default);
    Task<BrdWorkflowResult> RequestChangesAsync(string projectId, string reviewer, string feedback, CancellationToken ct = default);
    Task<BrdWorkflowResult> GetStatusAsync(string projectId, CancellationToken ct = default);
}

public class BrdWorkflowService(
    GNexDbContext db,
    IAgentOrchestrator orchestrator,
    IBrdStatusNotifier notifier,
    ILogger<BrdWorkflowService> logger) : IBrdWorkflowService
{
    public async Task<BrdWorkflowResult> GetStatusAsync(string projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project is null)
            return new BrdWorkflowResult(false, "Project not found.", null);

        var sectionCount = await db.BrdSectionRecords
            .CountAsync(s => s.BrdId == projectId && s.IsActive, ct);

        return new BrdWorkflowResult(true, project.BrdStatus, project.BrdStatus, sectionCount,
            project.BrdApprovedAt, project.BrdApprovedBy);
    }

    public async Task<BrdWorkflowResult> SubmitForReviewAsync(string projectId, string reviewer, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project is null)
            return new BrdWorkflowResult(false, "Project not found.", null);

        if (project.BrdStatus != "draft")
            return new BrdWorkflowResult(false, $"Cannot submit: BRD is '{project.BrdStatus}', expected 'draft'.", project.BrdStatus);

        // Validate: must have sections
        var sectionCount = await db.BrdSectionRecords
            .CountAsync(s => s.BrdId == projectId && s.IsActive, ct);
        if (sectionCount == 0)
            return new BrdWorkflowResult(false, "Cannot submit: No BRD sections exist.", project.BrdStatus);

        project.BrdStatus = "in_review";
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedBy = reviewer;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD submitted for review: project={ProjectId}, reviewer={Reviewer}", projectId, reviewer);
        await notifier.NotifyBrdStatusChangedAsync(projectId, "in_review", "BRD submitted for review", ct);
        return new BrdWorkflowResult(true, "BRD submitted for review.", "in_review", sectionCount);
    }

    public async Task<BrdWorkflowResult> ApproveAsync(string projectId, string reviewer, string? comment = null, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project is null)
            return new BrdWorkflowResult(false, "Project not found.", null);

        if (project.BrdStatus != "in_review")
            return new BrdWorkflowResult(false, $"Cannot approve: BRD is '{project.BrdStatus}', expected 'in_review'.", project.BrdStatus);

        project.BrdStatus = "approved";
        project.BrdApprovedAt = DateTimeOffset.UtcNow;
        project.BrdApprovedBy = reviewer;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedBy = reviewer;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD approved: project={ProjectId}, reviewer={Reviewer}. Triggering pipeline.", projectId, reviewer);
        await notifier.NotifyBrdStatusChangedAsync(projectId, "approved", "BRD approved. Pipeline triggered.", ct);

        // Fire-and-forget the pipeline trigger on a background thread
        _ = Task.Run(async () =>
        {
            try
            {
                await TriggerPipelineForApprovedBrdAsync(projectId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pipeline trigger failed for approved BRD: {ProjectId}", projectId);
            }
        }, CancellationToken.None);

        var sectionCount = await db.BrdSectionRecords
            .CountAsync(s => s.BrdId == projectId && s.IsActive, ct);
        return new BrdWorkflowResult(true, "BRD approved. Pipeline triggered.", "approved", sectionCount,
            project.BrdApprovedAt, project.BrdApprovedBy);
    }

    public async Task<BrdWorkflowResult> RejectAsync(string projectId, string reviewer, string reason, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project is null)
            return new BrdWorkflowResult(false, "Project not found.", null);

        if (project.BrdStatus != "in_review")
            return new BrdWorkflowResult(false, $"Cannot reject: BRD is '{project.BrdStatus}', expected 'in_review'.", project.BrdStatus);

        project.BrdStatus = "rejected";
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedBy = reviewer;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD rejected: project={ProjectId}, reason={Reason}", projectId, reason);
        await notifier.NotifyBrdStatusChangedAsync(projectId, "rejected", $"BRD rejected: {reason}", ct);
        return new BrdWorkflowResult(true, $"BRD rejected: {reason}", "rejected");
    }

    public async Task<BrdWorkflowResult> RequestChangesAsync(string projectId, string reviewer, string feedback, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project is null)
            return new BrdWorkflowResult(false, "Project not found.", null);

        if (project.BrdStatus != "in_review")
            return new BrdWorkflowResult(false, $"Cannot request changes: BRD is '{project.BrdStatus}', expected 'in_review'.", project.BrdStatus);

        project.BrdStatus = "draft";
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedBy = reviewer;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD changes requested: project={ProjectId}, feedback={Feedback}", projectId, feedback);
        await notifier.NotifyBrdStatusChangedAsync(projectId, "draft", $"Changes requested: {feedback}", ct);
        return new BrdWorkflowResult(true, $"Changes requested: {feedback}", "draft");
    }

    private async Task TriggerPipelineForApprovedBrdAsync(string projectId, CancellationToken ct)
    {
        logger.LogInformation("Starting pipeline for approved BRD: {ProjectId}", projectId);

        var config = new PipelineConfig
        {
            ProjectId = projectId,
            RequirementsPath = string.Empty, // RequirementsReader will use BRD sections via ReadFromBrdAsync
            OutputPath = $"output/{projectId}",
            SolutionNamespace = "GNex",
            EnableIntegrationLayer = true,
            EnableTestGeneration = true,
            EnableReviewAgent = true
        };

        await orchestrator.RunProjectPipelineAsync(projectId, config, ct);
        logger.LogInformation("Pipeline completed for BRD project: {ProjectId}", projectId);
    }
}

public sealed record BrdWorkflowResult(
    bool Success,
    string Message,
    string? Status,
    int SectionCount = 0,
    DateTimeOffset? ApprovedAt = null,
    string? ApprovedBy = null);
