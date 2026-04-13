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
    Task NotifyBrdStatusChangedAsync(string projectId, string brdId, string brdTitle, string status, string message, CancellationToken ct = default);
}

public interface IBrdWorkflowService
{
    Task<BrdWorkflowResult> SubmitForReviewAsync(string brdId, string reviewer, CancellationToken ct = default);
    Task<BrdWorkflowResult> ApproveAsync(string brdId, string reviewer, string? comment = null, CancellationToken ct = default);
    Task<BrdWorkflowResult> RejectAsync(string brdId, string reviewer, string reason, CancellationToken ct = default);
    Task<BrdWorkflowResult> RequestChangesAsync(string brdId, string reviewer, string feedback, CancellationToken ct = default);
    Task<BrdWorkflowResult> GetStatusAsync(string brdId, CancellationToken ct = default);
}

public class BrdWorkflowService(
    GNexDbContext db,
    IAgentOrchestrator orchestrator,
    IBrdStatusNotifier notifier,
    ILogger<BrdWorkflowService> logger) : IBrdWorkflowService
{
    public async Task<BrdWorkflowResult> GetStatusAsync(string brdId, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null)
            return new BrdWorkflowResult(false, "BRD document not found.", null);

        var sectionCount = await db.BrdSectionRecords
            .CountAsync(s => s.BrdId == brdId && s.IsActive, ct);

        return new BrdWorkflowResult(true, doc.Status, doc.Status, sectionCount,
            doc.ApprovedAt, doc.ApprovedBy);
    }

    public async Task<BrdWorkflowResult> SubmitForReviewAsync(string brdId, string reviewer, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null)
            return new BrdWorkflowResult(false, "BRD document not found.", null);

        if (doc.Status is not "draft" and not "enriched")
            return new BrdWorkflowResult(false, $"Cannot submit: BRD is '{doc.Status}', expected 'draft' or 'enriched'.", doc.Status);

        var sectionCount = await db.BrdSectionRecords
            .CountAsync(s => s.BrdId == brdId && s.IsActive, ct);
        if (sectionCount == 0)
            return new BrdWorkflowResult(false, "Cannot submit: No BRD sections exist.", doc.Status);

        doc.Status = "in_review";
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD submitted for review: brd={BrdId}, reviewer={Reviewer}", brdId, reviewer);
        await notifier.NotifyBrdStatusChangedAsync(doc.ProjectId, doc.Id, doc.Title, "in_review", "BRD submitted for review", ct);
        return new BrdWorkflowResult(true, "BRD submitted for review.", "in_review", sectionCount);
    }

    public async Task<BrdWorkflowResult> ApproveAsync(string brdId, string reviewer, string? comment = null, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null)
            return new BrdWorkflowResult(false, "BRD document not found.", null);

        if (doc.Status != "in_review")
            return new BrdWorkflowResult(false, $"Cannot approve: BRD is '{doc.Status}', expected 'in_review'.", doc.Status);

        doc.Status = "approved";
        doc.ApprovedAt = DateTimeOffset.UtcNow;
        doc.ApprovedBy = reviewer;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD approved: brd={BrdId}, reviewer={Reviewer}. Triggering pipeline.", brdId, reviewer);
        await notifier.NotifyBrdStatusChangedAsync(doc.ProjectId, doc.Id, doc.Title, "approved", "BRD approved. Pipeline triggered.", ct);

        // Fire-and-forget the pipeline trigger
        _ = Task.Run(async () =>
        {
            try
            {
                await TriggerPipelineForApprovedBrdAsync(doc.ProjectId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pipeline trigger failed for approved BRD: {BrdId}", brdId);
            }
        }, CancellationToken.None);

        var sectionCount = await db.BrdSectionRecords
            .CountAsync(s => s.BrdId == brdId && s.IsActive, ct);
        return new BrdWorkflowResult(true, "BRD approved. Pipeline triggered.", "approved", sectionCount,
            doc.ApprovedAt, doc.ApprovedBy);
    }

    public async Task<BrdWorkflowResult> RejectAsync(string brdId, string reviewer, string reason, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null)
            return new BrdWorkflowResult(false, "BRD document not found.", null);

        if (doc.Status != "in_review")
            return new BrdWorkflowResult(false, $"Cannot reject: BRD is '{doc.Status}', expected 'in_review'.", doc.Status);

        doc.Status = "rejected";
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD rejected: brd={BrdId}, reason={Reason}", brdId, reason);
        await notifier.NotifyBrdStatusChangedAsync(doc.ProjectId, doc.Id, doc.Title, "rejected", $"BRD rejected: {reason}", ct);
        return new BrdWorkflowResult(true, $"BRD rejected: {reason}", "rejected");
    }

    public async Task<BrdWorkflowResult> RequestChangesAsync(string brdId, string reviewer, string feedback, CancellationToken ct = default)
    {
        var doc = await db.BrdDocuments.FirstOrDefaultAsync(d => d.Id == brdId && d.IsActive, ct);
        if (doc is null)
            return new BrdWorkflowResult(false, "BRD document not found.", null);

        if (doc.Status != "in_review")
            return new BrdWorkflowResult(false, $"Cannot request changes: BRD is '{doc.Status}', expected 'in_review'.", doc.Status);

        doc.Status = "draft";
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BRD changes requested: brd={BrdId}, feedback={Feedback}", brdId, feedback);
        await notifier.NotifyBrdStatusChangedAsync(doc.ProjectId, doc.Id, doc.Title, "draft", $"Changes requested: {feedback}", ct);
        return new BrdWorkflowResult(true, $"Changes requested: {feedback}", "draft");
    }

    private async Task TriggerPipelineForApprovedBrdAsync(string projectId, CancellationToken ct)
    {
        logger.LogInformation("Starting pipeline for approved BRD project: {ProjectId}", projectId);

        var config = new PipelineConfig
        {
            ProjectId = projectId,
            RequirementsPath = string.Empty,
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
