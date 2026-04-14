using GNex.Core.Interfaces;
using GNex.Database;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Repositories;
using GNex.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using FluentAssertions;

namespace GNex.Tests.Integration;

/// <summary>
/// Integration tests for BrdWorkflowService exercising the full state machine
/// (draft → enriched → in_review → approved/rejected/changes_requested)
/// against a real InMemory DbContext. Orchestrator &amp; notifier are mocked.
///
/// Scenarios: SaaS analytics dashboard, mobile banking app, AI chatbot platform.
/// </summary>
public sealed class BrdWorkflowIntegrationTests : IDisposable
{
    private readonly TestDbFixture _fix = new();
    private readonly Mock<IAgentOrchestrator> _orchestratorMock = new();
    private readonly Mock<IBrdStatusNotifier> _notifierMock = new();
    private readonly BrdWorkflowService _service;

    public BrdWorkflowIntegrationTests()
    {
        _orchestratorMock
            .Setup(o => o.RunProjectPipelineAsync(It.IsAny<string>(), It.IsAny<GNex.Core.Models.PipelineConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GNex.Core.Models.AgentContext());

        _notifierMock
            .Setup(n => n.NotifyBrdStatusChangedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new BrdWorkflowService(
            _fix.Db,
            _orchestratorMock.Object,
            _notifierMock.Object,
            NullLogger<BrdWorkflowService>.Instance);
    }

    [Fact]
    public async Task SubmitForReview_DraftBrdWithSections_Succeeds()
    {
        // Arrange: project + BRD + sections
        var (_, brd) = await SeedProjectAndBrd("Draft-Submit Test", "draft-submit", "draft");
        await SeedSections(brd.Id, 3);

        // Act
        var result = await _service.SubmitForReviewAsync(brd.Id, "reviewer@platform.io");

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be("in_review");
        result.SectionCount.Should().Be(3);

        _notifierMock.Verify(n => n.NotifyBrdStatusChangedAsync(
            It.IsAny<string>(), brd.Id, It.IsAny<string>(), "in_review", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitForReview_NoSections_Fails()
    {
        var (_, brd) = await SeedProjectAndBrd("No Sections Test", "no-sections", "draft");

        var result = await _service.SubmitForReviewAsync(brd.Id, "reviewer@platform.io");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No BRD sections exist");
    }

    [Fact]
    public async Task SubmitForReview_AlreadyApproved_Fails()
    {
        var (_, brd) = await SeedProjectAndBrd("Already Approved", "already-approved", "approved");

        var result = await _service.SubmitForReviewAsync(brd.Id, "reviewer@platform.io");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Cannot submit");
    }

    [Fact]
    public async Task SubmitForReview_EnrichedBrdWithSections_Succeeds()
    {
        var (_, brd) = await SeedProjectAndBrd("Enriched Submit", "enriched-submit", "enriched");
        await SeedSections(brd.Id, 5);

        var result = await _service.SubmitForReviewAsync(brd.Id, "reviewer@platform.io");

        result.Success.Should().BeTrue();
        result.Status.Should().Be("in_review");
    }

    [Fact]
    public async Task Approve_InReviewBrd_SetsApprovedAndTriggersNotifier()
    {
        var (_, brd) = await SeedProjectAndBrd("Approve Test", "approve-test", "in_review");
        await SeedSections(brd.Id, 2);

        var result = await _service.ApproveAsync(brd.Id, "tech-lead@platform.io", "Looks great!");

        result.Success.Should().BeTrue();
        result.Status.Should().Be("approved");
        result.ApprovedBy.Should().Be("tech-lead@platform.io");
        result.ApprovedAt.Should().NotBeNull();

        _notifierMock.Verify(n => n.NotifyBrdStatusChangedAsync(
            It.IsAny<string>(), brd.Id, It.IsAny<string>(), "approved", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_DraftBrd_Fails()
    {
        var (_, brd) = await SeedProjectAndBrd("Draft Approve Fail", "draft-approve-fail", "draft");

        var result = await _service.ApproveAsync(brd.Id, "reviewer@platform.io");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Cannot approve");
    }

    [Fact]
    public async Task Reject_InReviewBrd_SetsRejected()
    {
        var (_, brd) = await SeedProjectAndBrd("Reject Test", "reject-test", "in_review");

        var result = await _service.RejectAsync(brd.Id, "qa-lead@platform.io", "Missing security requirements for PCI-DSS compliance");

        result.Success.Should().BeTrue();
        result.Status.Should().Be("rejected");
        result.Message.Should().Contain("PCI-DSS");
    }

    [Fact]
    public async Task Reject_NotInReview_Fails()
    {
        var (_, brd) = await SeedProjectAndBrd("Draft Reject Fail", "draft-reject-fail", "draft");

        var result = await _service.RejectAsync(brd.Id, "reviewer@platform.io", "Reason");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Cannot reject");
    }

    [Fact]
    public async Task RequestChanges_InReviewBrd_RevertsToDraft()
    {
        var (_, brd) = await SeedProjectAndBrd("Changes Test", "changes-test", "in_review");

        var result = await _service.RequestChangesAsync(brd.Id, "architect@platform.io",
            "Section 3 needs more detail on API rate limiting and throttling strategy");

        result.Success.Should().BeTrue();
        result.Status.Should().Be("draft");
        result.Message.Should().Contain("rate limiting");
    }

    [Fact]
    public async Task GetStatus_ExistingBrd_ReturnsCorrectState()
    {
        var (_, brd) = await SeedProjectAndBrd("Status Check", "status-check", "enriched");
        await SeedSections(brd.Id, 4);

        var result = await _service.GetStatusAsync(brd.Id);

        result.Success.Should().BeTrue();
        result.Status.Should().Be("enriched");
        result.SectionCount.Should().Be(4);
    }

    [Fact]
    public async Task GetStatus_NonExistentBrd_ReturnsNotFound()
    {
        var result = await _service.GetStatusAsync("nonexistent-brd-id");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task FullWorkflowCycle_DraftToApproved()
    {
        // Full cycle: draft → enriched → submit → in_review → approve → approved
        var (_, brd) = await SeedProjectAndBrd("Full Cycle SaaS Dashboard", "full-cycle-saas", "draft");
        await SeedSections(brd.Id, 6);

        // Enrichment phase (direct DB update simulating agent enrichment)
        brd.Status = "enriched";
        brd.UpdatedAt = DateTimeOffset.UtcNow;
        await _fix.Db.SaveChangesAsync();

        // Submit
        var submitResult = await _service.SubmitForReviewAsync(brd.Id, "reviewer@platform.io");
        submitResult.Success.Should().BeTrue();

        // Approve
        var approveResult = await _service.ApproveAsync(brd.Id, "vp-engineering@platform.io", "Ship it!");
        approveResult.Success.Should().BeTrue();
        approveResult.Status.Should().Be("approved");

        // Verify persistence
        var final = await _service.GetStatusAsync(brd.Id);
        final.Status.Should().Be("approved");
        final.ApprovedBy.Should().Be("vp-engineering@platform.io");
    }

    [Fact]
    public async Task ReviewCycle_RequestChangesAndResubmit()
    {
        var (_, brd) = await SeedProjectAndBrd("Revision Cycle", "revision-cycle", "in_review");
        await SeedSections(brd.Id, 3);

        // Request changes
        var changeResult = await _service.RequestChangesAsync(brd.Id, "reviewer@platform.io", "Add caching strategy");
        changeResult.Status.Should().Be("draft");

        // Re-enrich
        brd.Status = "enriched";
        await _fix.Db.SaveChangesAsync();

        // Re-submit
        var resubmit = await _service.SubmitForReviewAsync(brd.Id, "reviewer@platform.io");
        resubmit.Success.Should().BeTrue();
        resubmit.Status.Should().Be("in_review");

        // Approve on second review
        var approve = await _service.ApproveAsync(brd.Id, "reviewer@platform.io");
        approve.Success.Should().BeTrue();
        approve.Status.Should().Be("approved");
    }

    [Fact]
    public async Task SoftDeletedBrd_NotFoundByWorkflow()
    {
        var brdRepo = _fix.CreateRepo<BrdDocument>();
        var (_, brd) = await SeedProjectAndBrd("Soft Delete Test", "soft-delete-test", "draft");

        await brdRepo.SoftDeleteAsync(brd.Id);

        var result = await _service.GetStatusAsync(brd.Id);
        result.Success.Should().BeFalse();
    }

    // ── Helpers ──

    private async Task<(Project project, BrdDocument brd)> SeedProjectAndBrd(string projectName, string slug, string brdStatus)
    {
        var projectRepo = _fix.CreateRepo<Project>();
        var brdRepo = _fix.CreateRepo<BrdDocument>();

        var project = await projectRepo.CreateAsync(new Project
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = projectName,
            Slug = slug,
            ProjectType = "web_app",
            Status = "active"
        });

        var brd = await brdRepo.CreateAsync(new BrdDocument
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Title = $"{projectName} — Core Module BRD",
            BrdType = "general",
            Status = brdStatus
        });

        return (project, brd);
    }

    private async Task SeedSections(string brdId, int count)
    {
        var sectionTypes = new[] { "executive_summary", "functional_requirements", "non_functional_requirements", "data_model", "api_design", "deployment" };
        var sectionRepo = _fix.CreateRepo<BrdSectionRecord>();

        for (int i = 0; i < count; i++)
        {
            await sectionRepo.CreateAsync(new BrdSectionRecord
            {
                TenantId = TestDbFixture.TestTenantId,
                BrdId = brdId,
                SectionType = sectionTypes[i % sectionTypes.Length],
                Order = i + 1,
                Content = $"Section {i + 1} content for BRD analysis..."
            });
        }
    }

    public void Dispose() => _fix.Dispose();
}
