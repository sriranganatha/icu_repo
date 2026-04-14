using GNex.Database;
using GNex.Database.Entities.Platform;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace GNex.Tests.Integration;

/// <summary>
/// Integration tests covering the full project lifecycle on the GNex platform:
/// Create Project → Add Tech Stack → Create BRD → Enrich Sections →
/// Generate Backlog (Epics/Stories/Tasks) → Sprint Planning →
/// Agent Assignments → Agent Runs → Artifact Generation → Review
///
/// Realistic scenarios: fintech payment gateway, e-commerce marketplace, SaaS analytics dashboard.
/// </summary>
public sealed class ProjectLifecycleIntegrationTests : IDisposable
{
    private readonly TestDbFixture _fix = new();

    [Fact]
    public async Task FullProjectLifecycle_FintechPaymentGateway()
    {
        var projectRepo = _fix.CreateRepo<Project>();

        // ── Step 1: Create a fintech project ──
        var project = await projectRepo.CreateAsync(new Project
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "Apex Payment Gateway",
            Slug = "apex-payment-gw",
            Description = "Real-time payment processing platform supporting card, ACH, wire transfer with PCI-DSS compliance",
            ProjectType = "api",
            Status = "active"
        });

        project.Id.Should().NotBeNullOrEmpty();
        project.Slug.Should().Be("apex-payment-gw");

        // ── Step 2: Add tech stack ──
        var techStackRepo = _fix.CreateRepo<ProjectTechStack>();
        var techItems = new[]
        {
            new ProjectTechStack { TenantId = TestDbFixture.TestTenantId, ProjectId = project.Id, Layer = "backend", TechnologyId = "lang-csharp", TechnologyType = "language", Version = "12.0" },
            new ProjectTechStack { TenantId = TestDbFixture.TestTenantId, ProjectId = project.Id, Layer = "backend", TechnologyId = "fw-aspnet-core", TechnologyType = "framework", Version = "9.0" },
            new ProjectTechStack { TenantId = TestDbFixture.TestTenantId, ProjectId = project.Id, Layer = "database", TechnologyId = "db-postgresql", TechnologyType = "database", Version = "16.2" },
            new ProjectTechStack { TenantId = TestDbFixture.TestTenantId, ProjectId = project.Id, Layer = "cache", TechnologyId = "db-redis", TechnologyType = "database", Version = "7.2" },
        };
        foreach (var ts in techItems)
            await techStackRepo.CreateAsync(ts);

        var stack = await techStackRepo.QueryAsync(t => t.ProjectId == project.Id);
        stack.Should().HaveCount(4);

        // ── Step 3: Project settings ──
        var settingsRepo = _fix.CreateRepo<ProjectSettings>();
        var settings = await settingsRepo.CreateAsync(new ProjectSettings
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            GitRepoUrl = "https://github.com/apexpay/payment-gateway",
            DefaultBranch = "main",
            ArtifactStoragePath = "output/apex-payment-gw"
        });
        settings.DefaultBranch.Should().Be("main");

        // ── Step 4: Create BRD Document ──
        var brdRepo = _fix.CreateRepo<BrdDocument>();
        var brd = await brdRepo.CreateAsync(new BrdDocument
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Title = "Payment Processing Core Module",
            Description = "Handles card authorization, capture, settlement, refunds, and dispute management",
            BrdType = "api_service",
            Status = "draft"
        });

        brd.Id.Should().NotBeNullOrEmpty();
        brd.Status.Should().Be("draft");

        // ── Step 5: Add BRD sections (simulating AI enrichment) ──
        var sectionRepo = _fix.CreateRepo<BrdSectionRecord>();
        var sections = new[]
        {
            new BrdSectionRecord { TenantId = TestDbFixture.TestTenantId, BrdId = brd.Id, SectionType = "executive_summary", Order = 1, Content = "Apex Payment Gateway processes $2B+ in annual volume..." },
            new BrdSectionRecord { TenantId = TestDbFixture.TestTenantId, BrdId = brd.Id, SectionType = "functional_requirements", Order = 2, Content = "1. Card Authorization (Visa, MC, Amex)\n2. Real-time fraud scoring\n3. Multi-currency support..." },
            new BrdSectionRecord { TenantId = TestDbFixture.TestTenantId, BrdId = brd.Id, SectionType = "non_functional_requirements", Order = 3, Content = "P99 latency < 200ms, 99.99% uptime, PCI-DSS Level 1 compliance..." },
            new BrdSectionRecord { TenantId = TestDbFixture.TestTenantId, BrdId = brd.Id, SectionType = "data_model", Order = 4, Content = "Transaction, Merchant, PaymentMethod, Settlement, Dispute entities..." },
        };
        foreach (var s in sections)
            await sectionRepo.CreateAsync(s);

        // ── Step 6: BRD Workflow ──
        brd.Status = "enriched";
        await brdRepo.UpdateAsync(brd);

        brd.Status = "in_review";
        await brdRepo.UpdateAsync(brd);

        brd.Status = "approved";
        brd.ApprovedAt = DateTimeOffset.UtcNow;
        brd.ApprovedBy = "cto@apexpay.com";
        await brdRepo.UpdateAsync(brd);

        var approved = await brdRepo.GetByIdAsync(brd.Id);
        approved!.Status.Should().Be("approved");
        approved.ApprovedBy.Should().Be("cto@apexpay.com");

        // ── Step 7: Raw requirements ──
        var reqRepo = _fix.CreateRepo<RawRequirement>();
        var req = await reqRepo.CreateAsync(new RawRequirement
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            InputText = "Build a payment gateway that supports Visa, Mastercard, and American Express. Must process transactions in under 200ms with real-time fraud detection.",
            InputType = "text",
            SubmittedBy = "product-owner@apexpay.com"
        });
        req.InputText.Should().Contain("Visa");

        // ── Step 8: Enriched requirements ──
        var enrichedRepo = _fix.CreateRepo<EnrichedRequirement>();
        var enriched = await enrichedRepo.CreateAsync(new EnrichedRequirement
        {
            TenantId = TestDbFixture.TestTenantId,
            RawRequirementId = req.Id,
            EnrichedJson = """{"requirements":[{"id":"REQ-001","title":"Card Authorization","priority":"critical","acceptance_criteria":["Visa/MC/Amex supported","Auth response < 200ms","3DS v2 integration"]}]}""",
            ClarificationQuestionsJson = """["What card networks besides Visa/MC/Amex?","Target TPS?"]""",
            Version = 1
        });
        enriched.EnrichedJson.Should().Contain("Card Authorization");

        // ── Step 9: Create Epics ──
        var epicRepo = _fix.CreateRepo<Epic>();
        var epic = await epicRepo.CreateAsync(new Epic
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Title = "Payment Processing Engine",
            Description = "Core transaction processing: auth, capture, void, refund",
            Priority = "critical",
            Status = "ready",
            Order = 1
        });

        // ── Step 10: Create Stories ──
        var storyRepo = _fix.CreateRepo<Story>();
        var story1 = await storyRepo.CreateAsync(new Story
        {
            TenantId = TestDbFixture.TestTenantId,
            EpicId = epic.Id,
            Title = "As a merchant, I want to authorize a card payment so I can verify funds before capture",
            AcceptanceCriteriaJson = """["Supports Visa/MC/Amex","Response time < 200ms","Returns auth code on success","Handles declined gracefully"]""",
            StoryPoints = 13,
            Status = "ready",
            Order = 1
        });

        var story2 = await storyRepo.CreateAsync(new Story
        {
            TenantId = TestDbFixture.TestTenantId,
            EpicId = epic.Id,
            Title = "As the system, I want to perform real-time fraud scoring on every transaction",
            AcceptanceCriteriaJson = """["Score range 0-100","Block if score > 90","Flag for review if 70-90","Process in < 50ms"]""",
            StoryPoints = 8,
            Status = "backlog",
            Order = 2
        });

        // ── Step 11: Create Tasks ──
        var taskRepo = _fix.CreateRepo<TaskItem>();
        var task1 = await taskRepo.CreateAsync(new TaskItem
        {
            TenantId = TestDbFixture.TestTenantId,
            StoryId = story1.Id,
            TaskType = "code",
            AssignedAgentType = "ServiceLayer",
            EstimatedTokens = 15000,
            Status = "pending",
            Order = 1
        });

        var task2 = await taskRepo.CreateAsync(new TaskItem
        {
            TenantId = TestDbFixture.TestTenantId,
            StoryId = story1.Id,
            TaskType = "test",
            AssignedAgentType = "Testing",
            EstimatedTokens = 8000,
            DependsOnJson = $"""["{task1.Id}"]""",
            Status = "pending",
            Order = 2
        });

        // ── Step 12: Sprint planning ──
        var sprintRepo = _fix.CreateRepo<Sprint>();
        var sprint = await sprintRepo.CreateAsync(new Sprint
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Name = "Sprint 1 — Payment Auth Foundation",
            Goal = "Implement card authorization flow with Visa/MC support",
            Order = 1,
            Status = "active",
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddDays(14)
        });
        sprint.Status.Should().Be("active");

        // Assign story to sprint
        story1.SprintId = sprint.Id;
        await storyRepo.UpdateAsync(story1);

        // ── Step 13: Agent assignment ──
        var assignRepo = _fix.CreateRepo<AgentAssignment>();
        var assignment = await assignRepo.CreateAsync(new AgentAssignment
        {
            TenantId = TestDbFixture.TestTenantId,
            TaskId = task1.Id,
            AgentTypeDefinitionId = "agent-service-layer",
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow
        });

        // ── Step 14: Agent run ──
        var runRepo = _fix.CreateRepo<AgentRun>();
        var run = await runRepo.CreateAsync(new AgentRun
        {
            TenantId = TestDbFixture.TestTenantId,
            AssignmentId = assignment.Id,
            RunNumber = 1,
            InputJson = """{"story":"Card Authorization","tech_stack":"C# + ASP.NET Core"}""",
            Status = "succeeded",
            TokensUsed = 12_500,
            DurationMs = 45_000,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow
        });

        // ── Step 15: Artifact ──
        var artifactRepo = _fix.CreateRepo<AgentArtifactRecord>();
        var artifact = await artifactRepo.CreateAsync(new AgentArtifactRecord
        {
            TenantId = TestDbFixture.TestTenantId,
            RunId = run.Id,
            ProjectId = project.Id,
            ArtifactType = "code",
            FilePath = "src/Services/PaymentAuthorizationService.cs",
            ReviewStatus = "pending"
        });

        // ── Step 16: Review result ──
        var reviewRepo = _fix.CreateRepo<ReviewResult>();
        var review = await reviewRepo.CreateAsync(new ReviewResult
        {
            TenantId = TestDbFixture.TestTenantId,
            ArtifactId = artifact.Id,
            ReviewerAgentType = "CodeQuality",
            Verdict = "approved",
            Score = 92,
            CommentsJson = """["Clean separation of concerns","Good error handling","Consider adding circuit breaker for external calls"]"""
        });
        review.Verdict.Should().Be("approved");

        // ── Step 17: Quality report ──
        var qualityRepo = _fix.CreateRepo<QualityReport>();
        var report = await qualityRepo.CreateAsync(new QualityReport
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            SprintId = sprint.Id,
            CoveragePercent = 87.5m,
            LintErrors = 0,
            LintWarnings = 3,
            ComplexityScore = 12.4m,
            SecurityVulnerabilities = 0,
            DetailsJson = """{"passed_tests":142,"failed_tests":0,"skipped":2}"""
        });
        report.CoveragePercent.Should().Be(87.5m);

        // ── Step 18: Traceability ──
        var traceRepo = _fix.CreateRepo<TraceabilityRecord>();
        await traceRepo.CreateAsync(new TraceabilityRecord
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            RequirementId = req.Id,
            StoryId = story1.Id,
            TaskId = task1.Id,
            ArtifactId = artifact.Id,
            LinkType = "implements"
        });

        // ── Step 19: Project metrics ──
        var metricRepo = _fix.CreateRepo<ProjectMetric>();
        await metricRepo.CreateAsync(new ProjectMetric
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            MetricType = "token_usage",
            Value = 12_500,
            DimensionsJson = """{"agent_type":"ServiceLayer","sprint":"Sprint 1"}"""
        });

        // ── Verify full chain persisted ──
        var projectFetched = await projectRepo.GetByIdAsync(project.Id);
        projectFetched.Should().NotBeNull();
        projectFetched!.Name.Should().Be("Apex Payment Gateway");

        (await techStackRepo.CountAsync(t => t.ProjectId == project.Id)).Should().Be(4);
        (await sectionRepo.CountAsync(s => s.BrdId == brd.Id)).Should().Be(4);
        (await storyRepo.CountAsync(s => s.EpicId == epic.Id)).Should().Be(2);
        (await taskRepo.CountAsync(t => t.StoryId == story1.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Project_UniqueSlugPerTenant_IndexConfigured()
    {
        var model = _fix.Db.Model.FindEntityType(typeof(Project));
        var slugIndex = model?.GetIndexes().FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == "Slug") &&
            i.Properties.Any(p => p.Name == "TenantId"));

        slugIndex.Should().NotBeNull("unique index on (TenantId, Slug) should be configured");
        slugIndex!.IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task BrdDocument_MultipleBrdsPerProject_IndependentStatuses()
    {
        var projectRepo = _fix.CreateRepo<Project>();
        var brdRepo = _fix.CreateRepo<BrdDocument>();

        var project = await projectRepo.CreateAsync(new Project
        {
            TenantId = TestDbFixture.TestTenantId,
            Name = "ShopWave E-Commerce Platform",
            Slug = "shopwave-ecommerce",
            ProjectType = "full_stack",
            Status = "active"
        });

        await brdRepo.CreateAsync(new BrdDocument
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Title = "Product Catalog & Search",
            BrdType = "web_application",
            Status = "approved",
            GroupId = "GRP-SHOPWAVE-001"
        });

        await brdRepo.CreateAsync(new BrdDocument
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Title = "Shopping Cart & Checkout",
            BrdType = "web_application",
            Status = "in_review",
            GroupId = "GRP-SHOPWAVE-001"
        });

        await brdRepo.CreateAsync(new BrdDocument
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = project.Id,
            Title = "Order Fulfillment API",
            BrdType = "api_service",
            Status = "draft"
        });

        var allBrds = await brdRepo.QueryAsync(b => b.ProjectId == project.Id);
        allBrds.Should().HaveCount(3);
        allBrds.Select(b => b.Status).Should().Contain(new[] { "approved", "in_review", "draft" });
    }

    [Fact]
    public async Task TaskDependency_FinishToStart_Chain()
    {
        var taskRepo = _fix.CreateRepo<TaskItem>();
        var depRepo = _fix.CreateRepo<TaskDependency>();
        var storyId = Guid.NewGuid().ToString("N");

        var taskA = await taskRepo.CreateAsync(new TaskItem
        {
            TenantId = TestDbFixture.TestTenantId,
            StoryId = storyId,
            TaskType = "code",
            Status = "pending",
            Order = 1
        });

        var taskB = await taskRepo.CreateAsync(new TaskItem
        {
            TenantId = TestDbFixture.TestTenantId,
            StoryId = storyId,
            TaskType = "test",
            Status = "pending",
            Order = 2
        });

        var taskC = await taskRepo.CreateAsync(new TaskItem
        {
            TenantId = TestDbFixture.TestTenantId,
            StoryId = storyId,
            TaskType = "review",
            Status = "pending",
            Order = 3
        });

        await depRepo.CreateAsync(new TaskDependency
        {
            TenantId = TestDbFixture.TestTenantId,
            TaskId = taskB.Id,
            DependsOnTaskId = taskA.Id,
            DependencyType = "finish_to_start"
        });

        await depRepo.CreateAsync(new TaskDependency
        {
            TenantId = TestDbFixture.TestTenantId,
            TaskId = taskC.Id,
            DependsOnTaskId = taskB.Id,
            DependencyType = "finish_to_start"
        });

        var deps = await depRepo.ListAsync(0, 50);
        deps.Should().HaveCount(2);
    }

    [Fact]
    public async Task BrdFeedbackRecord_ReviewCycle()
    {
        var brdRepo = _fix.CreateRepo<BrdDocument>();
        var sectionRepo = _fix.CreateRepo<BrdSectionRecord>();
        var feedbackRepo = _fix.CreateRepo<BrdFeedbackRecord>();

        var brd = await brdRepo.CreateAsync(new BrdDocument
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = Guid.NewGuid().ToString("N"),
            Title = "Analytics Dashboard BRD",
            BrdType = "web_application",
            Status = "in_review"
        });

        var section = await sectionRepo.CreateAsync(new BrdSectionRecord
        {
            TenantId = TestDbFixture.TestTenantId,
            BrdId = brd.Id,
            SectionType = "data_model",
            Order = 1,
            Content = "The analytics system stores event data in a time-series DB..."
        });

        var feedback = await feedbackRepo.CreateAsync(new BrdFeedbackRecord
        {
            TenantId = TestDbFixture.TestTenantId,
            BrdId = brd.Id,
            SectionId = section.Id,
            FeedbackText = "The data retention policy is not specified. Add 90-day hot, 2-year cold storage tiers.",
            Resolved = false
        });

        // Resolve feedback
        feedback.Resolved = true;
        feedback.ResolvedInVersion = 2;
        await feedbackRepo.UpdateAsync(feedback);

        var resolved = await feedbackRepo.GetByIdAsync(feedback.Id);
        resolved!.Resolved.Should().BeTrue();
        resolved.ResolvedInVersion.Should().Be(2);
    }

    [Fact]
    public async Task AgentConversation_RecordMultiTurn()
    {
        var convRepo = _fix.CreateRepo<AgentConversation>();
        var runId = Guid.NewGuid().ToString("N");

        var conv = await convRepo.CreateAsync(new AgentConversation
        {
            TenantId = TestDbFixture.TestTenantId,
            RunId = runId,
            MessagesJson = """[{"role":"system","content":"You are a code generator for fintech..."},{"role":"user","content":"Generate PaymentService.cs"},{"role":"assistant","content":"public class PaymentService { ... }"}]""",
            MessageCount = 3,
            TotalTokens = 4500
        });

        var fetched = await convRepo.GetByIdAsync(conv.Id);
        fetched!.MessageCount.Should().Be(3);
        fetched.TotalTokens.Should().Be(4500);
    }

    [Fact]
    public async Task EnvironmentConfig_MultiEnv()
    {
        var repo = _fix.CreateRepo<EnvironmentConfig>();
        var projectId = Guid.NewGuid().ToString("N");

        await repo.CreateAsync(new EnvironmentConfig
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = projectId,
            EnvName = "dev",
            VariablesJson = """{"DB_HOST":"localhost","DB_PORT":"5432","LOG_LEVEL":"Debug"}"""
        });

        await repo.CreateAsync(new EnvironmentConfig
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = projectId,
            EnvName = "staging",
            VariablesJson = """{"DB_HOST":"staging-db.internal","DB_PORT":"5432","LOG_LEVEL":"Information"}"""
        });

        await repo.CreateAsync(new EnvironmentConfig
        {
            TenantId = TestDbFixture.TestTenantId,
            ProjectId = projectId,
            EnvName = "prod",
            VariablesJson = """{"DB_HOST":"prod-db.internal","DB_PORT":"5432","LOG_LEVEL":"Warning"}""",
            InfraConfigJson = """{"replicas":3,"auto_scaling":true,"cdn_enabled":true}"""
        });

        var envs = await repo.QueryAsync(e => e.ProjectId == projectId);
        envs.Should().HaveCount(3);
    }

    public void Dispose() => _fix.Dispose();
}
