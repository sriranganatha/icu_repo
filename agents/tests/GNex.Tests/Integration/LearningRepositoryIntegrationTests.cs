using GNex.Database;
using GNex.Database.Entities.Platform.AgentRegistry;
using GNex.Database.Repositories;
using FluentAssertions;

namespace GNex.Tests.Integration;

/// <summary>
/// Integration tests for LearningRepository covering:
///   3-tier scope (Project→Domain→Global), deduplication on SaveBatch,
///   auto-promotion by project/domain spread, confidence recalculation,
///   combined pipeline loading, verify/deprecate lifecycle.
///
/// Scenarios: cross-project code gen patterns, domain-specific API design lessons.
/// </summary>
public sealed class LearningRepositoryIntegrationTests : IDisposable
{
    private readonly TestDbFixture _fix = new();
    private readonly LearningRepository _repo;

    public LearningRepositoryIntegrationTests()
    {
        _repo = new LearningRepository(_fix.Db);
    }

    [Fact]
    public async Task SaveBatch_NewLearnings_PersistsCorrectly()
    {
        var learnings = new[]
        {
            CreateLearning("proj-001", "CodeGenerator", "fintech",
                "Missing null check on payment amount",
                "Add guard clause: if (amount <= 0) throw ArgumentOutOfRangeException"),
            CreateLearning("proj-001", "Testing", "fintech",
                "Integration tests missing for retry logic",
                "Generate Polly-based retry tests for all HTTP clients")
        };

        await _repo.SaveBatchAsync(learnings);

        var results = await _repo.GetByProjectAsync("proj-001");
        results.Should().HaveCount(2);
        results.Should().Contain(l => l.AgentTypeCode == "CodeGenerator");
        results.Should().Contain(l => l.AgentTypeCode == "Testing");
    }

    [Fact]
    public async Task SaveBatch_Duplicate_IncrementsRecurrence()
    {
        var learning1 = CreateLearning("proj-001", "ServiceLayer", "fintech",
            "Service classes missing IDisposable implementation",
            "Implement IDisposable for services holding DB connections");

        var learning2 = CreateLearning("proj-001", "ServiceLayer", "fintech",
            "Service classes missing IDisposable implementation",
            "Implement IDisposable for services holding DB connections");

        await _repo.SaveBatchAsync(new[] { learning1 });
        await _repo.SaveBatchAsync(new[] { learning2 });

        var results = await _repo.GetByProjectAsync("proj-001");
        var match = results.Single(l => l.Problem.Contains("IDisposable"));
        match.Recurrence.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task SaveBatch_DuplicateFromDifferentProjects_TracksSpread()
    {
        // Same problem seen in project A and project B
        var learningA = CreateLearning("proj-fintech-001", "CodeGenerator", "fintech",
            "API controllers missing [ProducesResponseType] attributes",
            "Add [ProducesResponseType] for 200, 400, 404, 500");

        var learningB = CreateLearning("proj-fintech-002", "CodeGenerator", "fintech",
            "API controllers missing [ProducesResponseType] attributes",
            "Add [ProducesResponseType] for 200, 400, 404, 500");

        await _repo.SaveBatchAsync(new[] { learningA });
        await _repo.SaveBatchAsync(new[] { learningB });

        // After 2 projects, should promote to Domain scope
        var results = await _repo.GetByDomainAsync("fintech");
        results.Should().Contain(l => l.Problem.Contains("[ProducesResponseType]"));
    }

    [Fact]
    public async Task AutoPromotion_ThreeProjects_PromotesToGlobal()
    {
        var problem = "Entity classes missing audit timestamp properties";
        var resolution = "Add CreatedAt, UpdatedAt to all entities";

        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-001", "Database", "fintech", problem, resolution) });
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-002", "Database", "ecommerce", problem, resolution) });
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-003", "Database", "healthcare", problem, resolution) });

        var globalLearnings = await _repo.GetGlobalAsync();
        globalLearnings.Should().Contain(l => l.Problem.Contains("audit timestamp"));
        var match = globalLearnings.First(l => l.Problem.Contains("audit timestamp"));
        match.Scope.Should().Be(2); // Global
    }

    [Fact]
    public async Task GetCombinedForPipeline_LoadsAllThreeTiers()
    {
        // Project-scope learning
        await _repo.SaveBatchAsync(new[]
        {
            CreateLearning("proj-saas-001", "CodeGenerator", "saas",
                "SaaS projects need tenant context in every service",
                "Inject ITenantProvider into all services")
        });

        // Domain-scope learning (seen in 2 projects in same domain)
        var domainProblem = "SaaS billing integration missing idempotency keys";
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-saas-001", "Integration", "saas", domainProblem, "Add idempotency key header") });
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-saas-002", "Integration", "saas", domainProblem, "Add idempotency key header") });

        // Global-scope learning (seen across 3+ projects)
        var globalProblem = "Generated code missing XML documentation comments";
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-001", "CodeGenerator", "fintech", globalProblem, "Add /// comments") });
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-002", "CodeGenerator", "ecommerce", globalProblem, "Add /// comments") });
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-003", "CodeGenerator", "healthcare", globalProblem, "Add /// comments") });

        var combined = await _repo.GetCombinedForPipelineAsync("proj-saas-001", "saas");

        combined.Should().NotBeEmpty();
        // Should contain learnings from all 3 tiers
        combined.Should().Contain(l => l.Problem.Contains("tenant context")); // project
        combined.Should().Contain(l => l.Problem.Contains("idempotency")); // domain
        combined.Should().Contain(l => l.Problem.Contains("XML documentation")); // global
    }

    [Fact]
    public async Task GetForAgent_FiltersbyAgentType()
    {
        await _repo.SaveBatchAsync(new[]
        {
            CreateLearning("proj-001", "CodeGenerator", "fintech",
                "Missing input validation on DTOs",
                "Add FluentValidation validators"),
            CreateLearning("proj-001", "Testing", "fintech",
                "Tests not covering edge cases for null inputs",
                "Add parameterized tests for null/empty inputs"),
            CreateLearning("proj-001", "CodeGenerator", "fintech",
                "Hardcoded connection strings in services",
                "Use IConfiguration injection instead")
        });

        var codeGenLearnings = await _repo.GetForAgentAsync("proj-001", "CodeGenerator");
        codeGenLearnings.Should().HaveCount(2);
        codeGenLearnings.Should().OnlyContain(l => l.AgentTypeCode == "CodeGenerator");
    }

    [Fact]
    public async Task Verify_SetsVerifiedAndBoostsConfidence()
    {
        var learning = CreateLearning("proj-001", "Security", "fintech",
            "SQL injection vulnerability in raw query builders",
            "Always use parameterized queries via EF Core");
        await _repo.SaveBatchAsync(new[] { learning });

        var saved = (await _repo.GetByProjectAsync("proj-001")).First();
        var confidenceBefore = saved.Confidence;

        await _repo.VerifyAsync(saved.Id);

        // Re-fetch
        var verified = (await _repo.GetByProjectAsync("proj-001")).First(l => l.Id == saved.Id);
        verified.IsVerified.Should().BeTrue();
        verified.Confidence.Should().BeGreaterThan(confidenceBefore);
    }

    [Fact]
    public async Task Deprecate_ExcludesFromAllQueries()
    {
        var learning = CreateLearning("proj-001", "Deploy", "fintech",
            "Docker compose v1 syntax deprecated",
            "Migrate to Docker Compose V2 format");
        await _repo.SaveBatchAsync(new[] { learning });

        var saved = (await _repo.GetByProjectAsync("proj-001")).First();
        await _repo.DeprecateAsync(saved.Id);

        var results = await _repo.GetByProjectAsync("proj-001");
        results.Should().NotContain(l => l.Id == saved.Id);

        var combined = await _repo.GetCombinedForPipelineAsync("proj-001", "fintech");
        combined.Should().NotContain(l => l.Id == saved.Id);
    }

    [Fact]
    public async Task SaveBatch_UpdatesBetterResolution()
    {
        var shortResolution = CreateLearning("proj-001", "Architecture", "fintech",
            "Missing circuit breaker for external API calls",
            "Add retry");
        await _repo.SaveBatchAsync(new[] { shortResolution });

        // Later, a longer/better resolution is found
        var betterResolution = CreateLearning("proj-001", "Architecture", "fintech",
            "Missing circuit breaker for external API calls",
            "Implement Polly circuit breaker with 5-failure threshold, 30s break duration, and fallback to cached data");
        await _repo.SaveBatchAsync(new[] { betterResolution });

        var result = (await _repo.GetByProjectAsync("proj-001")).First(l => l.Problem.Contains("circuit breaker"));
        result.Resolution.Should().Contain("Polly circuit breaker");
    }

    [Fact]
    public async Task ConfidenceScoring_IncreasesWithRecurrenceAndVerification()
    {
        var learning = CreateLearning("proj-001", "CodeQuality", "general",
            "Cyclomatic complexity too high in service methods",
            "Extract complex logic into strategy pattern");
        await _repo.SaveBatchAsync(new[] { learning });

        var initial = (await _repo.GetByProjectAsync("proj-001")).First();
        var initialConfidence = initial.Confidence;

        // Add from another project
        await _repo.SaveBatchAsync(new[] { CreateLearning("proj-002", "CodeQuality", "general", initial.Problem, initial.Resolution) });
        var afterSpread = (await _repo.GetByProjectAsync("proj-001")).First(l => l.Problem == initial.Problem);
        afterSpread.Confidence.Should().BeGreaterThanOrEqualTo(initialConfidence);

        // Capture value before verify mutates the tracked entity
        var confidenceBeforeVerify = afterSpread.Confidence;

        // Verify
        await _repo.VerifyAsync(afterSpread.Id);
        var afterVerify = (await _repo.GetByProjectAsync("proj-001")).First(l => l.Id == afterSpread.Id);
        afterVerify.Confidence.Should().BeGreaterThan(confidenceBeforeVerify);
    }

    [Fact]
    public async Task GetByProject_RespectMaxItems()
    {
        // Create more than maxItems learnings
        var learnings = Enumerable.Range(1, 10).Select(i =>
            CreateLearning("proj-limit", "CodeGenerator", "general",
                $"Problem number {i}",
                $"Resolution number {i}")).ToArray();
        await _repo.SaveBatchAsync(learnings);

        var results = await _repo.GetByProjectAsync("proj-limit", maxItems: 5);
        results.Should().HaveCount(5);
    }

    // ── Helpers ──

    private static AgentLearning CreateLearning(string projectId, string agentType, string domain,
        string problem, string resolution) =>
        new()
        {
            ProjectId = projectId,
            AgentTypeCode = agentType,
            Domain = domain,
            Category = "code_quality",
            Problem = problem,
            Resolution = resolution,
            Impact = "medium",
            TargetAgents = agentType,
            PromptRule = $"When generating code, ensure: {resolution}",
            Recurrence = 1,
            Scope = 0, // Project scope
            Confidence = 0.3,
            IsVerified = false,
            IsDeprecated = false,
            SeenInProjects = "",
            SeenInDomains = ""
        };

    public void Dispose() => _fix.Dispose();
}
