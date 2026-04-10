using System.Diagnostics;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GNex.Agents.Supervisor;

/// <summary>
/// The Supervisor Agent is responsible for:
/// 1. Validating every other agent produced correct output (diagnostics)
/// 2. Detecting failures, stalls, and quality issues
/// 3. Taking corrective measures (remediation) and re-running agents
/// 4. Reporting test diagnostics with pass/fail/remediated status
/// </summary>
public sealed class SupervisorAgent : IAgent
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SupervisorAgent> _logger;

    public AgentType Type => AgentType.Supervisor;
    public string Name => "Supervisor Agent";
    public string Description => "Monitors all agents, runs diagnostics, triggers remediation on failure, ensures pipeline health.";

    private const int MaxRetries = 3;

    public SupervisorAgent(IServiceProvider serviceProvider, ILogger<SupervisorAgent> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private IEnumerable<IAgent> GetOtherAgents() =>
        _serviceProvider.GetServices<IAgent>().Where(a => a.Type != AgentType.Supervisor);

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("SupervisorAgent starting — validating pipeline health");

        var diagnostics = new List<TestDiagnostic>();

        try
        {
            // ── Phase 1: Validate each agent produced expected output ────
            if (context.ReportProgress is not null)
                await context.ReportProgress(Type, "Phase 1: Validating each agent output — Requirements, Database, ServiceLayer, Application, Integration, Testing, Review");
            diagnostics.AddRange(ValidateRequirementsAgent(context));
            diagnostics.AddRange(ValidateDatabaseAgent(context));
            diagnostics.AddRange(ValidateServiceLayerAgent(context));
            diagnostics.AddRange(ValidateApplicationAgent(context));
            diagnostics.AddRange(ValidateIntegrationAgent(context));
            diagnostics.AddRange(ValidateTestingAgent(context));
            diagnostics.AddRange(ValidateReviewAgent(context));

            // ── Phase 2: Cross-cutting pipeline health checks ────────────
            if (context.ReportProgress is not null)
            {
                var p1Passed = diagnostics.Count(d => d.Outcome == TestOutcome.Passed);
                var p1Failed = diagnostics.Count(d => d.Outcome == TestOutcome.Failed);
                await context.ReportProgress(Type, $"Phase 1 done: {p1Passed} passed, {p1Failed} failed. Phase 2: Cross-cutting pipeline integrity checks");
            }
            diagnostics.AddRange(ValidatePipelineIntegrity(context));
            diagnostics.AddRange(ValidateNoStaleAgents(context));

            // ── Phase 3: Attempt remediation for failed checks ───────────
            var failedDiags = diagnostics.Where(d => d.Outcome == TestOutcome.Failed).ToList();
            if (failedDiags.Count > 0)
            {
                _logger.LogWarning("SupervisorAgent found {Count} failing checks — attempting remediation", failedDiags.Count);
                if (context.ReportProgress is not null)
                    await context.ReportProgress(Type, $"Phase 3: {failedDiags.Count} failed checks — attempting auto-remediation");
                var remediations = await AttemptRemediationsAsync(failedDiags, context, ct);
                diagnostics.AddRange(remediations);
            }

            context.TestDiagnostics.AddRange(diagnostics);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            var passed = diagnostics.Count(d => d.Outcome == TestOutcome.Passed);
            var failed = diagnostics.Count(d => d.Outcome == TestOutcome.Failed);
            var remediated = diagnostics.Count(d => d.Outcome == TestOutcome.Remediated);
            var skipped = diagnostics.Count(d => d.Outcome == TestOutcome.Skipped);

            return new AgentResult
            {
                Agent = Type,
                Success = true,  // Supervisor always succeeds — failed checks are informational diagnostics, not agent crashes
                Summary = $"Supervisor: {diagnostics.Count} checks — {passed} passed, {failed} failed, {remediated} remediated, {skipped} skipped",
                TestDiagnostics = diagnostics,
                Messages =
                [
                    new AgentMessage
                    {
                        From = Type, To = AgentType.Orchestrator,
                        Subject = "Supervisor report",
                        Body = $"{passed} passed, {failed} failed, {remediated} remediated of {diagnostics.Count} total checks."
                    }
                ],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "SupervisorAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Agent-specific validation
    // ═══════════════════════════════════════════════════════════════════

    private List<TestDiagnostic> ValidateRequirementsAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "RequirementsReader";

        results.Add(CheckAgentCompleted(ctx, AgentType.RequirementsReader, agentName));

        results.Add(new TestDiagnostic
        {
            TestName = "Requirements_NonEmpty",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = ctx.Requirements.Count > 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = ctx.Requirements.Count > 0
                ? $"{ctx.Requirements.Count} requirements extracted"
                : "No requirements extracted — downstream agents will have nothing to process",
            Remediation = ctx.Requirements.Count > 0 ? "N/A" : "Check requirementsPath points to valid markdown files"
        });

        results.Add(new TestDiagnostic
        {
            TestName = "Requirements_HaveIds",
            AgentUnderTest = agentName,
            Category = "Data Quality",
            Outcome = ctx.Requirements.TrueForAll(r => !string.IsNullOrEmpty(r.Id)) ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = ctx.Requirements.TrueForAll(r => !string.IsNullOrEmpty(r.Id))
                ? "All requirements have IDs"
                : $"{ctx.Requirements.Count(r => string.IsNullOrEmpty(r.Id))} requirements missing ID",
            Remediation = "RequirementsReader must assign IDs from markdown headings or auto-generate them"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateDatabaseAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "Database";

        results.Add(CheckAgentCompleted(ctx, AgentType.Database, agentName));

        var dbArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Database).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Database_ProducedArtifacts",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = dbArtifacts.Count > 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = dbArtifacts.Count > 0 ? $"{dbArtifacts.Count} DB artifacts generated" : "No database artifacts produced",
            Remediation = "DatabaseAgent must run after requirements are loaded"
        });

        var hasEntities = dbArtifacts.Any(a => a.Layer == ArtifactLayer.Database && a.Content.Contains("class"));
        results.Add(new TestDiagnostic
        {
            TestName = "Database_HasEntityClasses",
            AgentUnderTest = agentName,
            Category = "Content Validation",
            Outcome = hasEntities ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasEntities ? "Entity classes found in DB artifacts" : "No C# entity classes found in DB layer",
            Remediation = "DatabaseAgent entity generation may have been skipped"
        });

        var hasDbContext = dbArtifacts.Any(a => a.Content.Contains("DbContext"));
        results.Add(new TestDiagnostic
        {
            TestName = "Database_HasDbContext",
            AgentUnderTest = agentName,
            Category = "Content Validation",
            Outcome = hasDbContext ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasDbContext ? "DbContext found" : "No DbContext class in database artifacts",
            Remediation = "DatabaseAgent must generate per-service DbContext"
        });

        var hasTenantId = dbArtifacts.Where(a => a.Layer == ArtifactLayer.Database)
            .All(a => !a.Content.Contains("class") || a.Content.Contains("TenantId"));
        results.Add(new TestDiagnostic
        {
            TestName = "Database_TenantIsolation",
            AgentUnderTest = agentName,
            Category = "Security",
            Outcome = hasTenantId ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasTenantId ? "All entities have TenantId" : "Some entities missing TenantId — tenant data leak risk",
            Remediation = "Add TenantId property to all regulated entity classes"
        });

        var hasMigrations = dbArtifacts.Any(a => a.Layer == ArtifactLayer.Migration);
        results.Add(new TestDiagnostic
        {
            TestName = "Database_HasMigrations",
            AgentUnderTest = agentName,
            Category = "Completeness",
            Outcome = hasMigrations ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasMigrations ? "Migration scripts present" : "No migration artifacts — schema cannot be deployed",
            Remediation = "DatabaseAgent must generate SQL migration scripts for each service schema"
        });

        var hasDockerCompose = dbArtifacts.Any(a => a.FileName.Contains("docker-compose"));
        results.Add(new TestDiagnostic
        {
            TestName = "Database_HasDockerCompose",
            AgentUnderTest = agentName,
            Category = "Infrastructure",
            Outcome = hasDockerCompose ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasDockerCompose ? "docker-compose.yml generated" : "No docker-compose artifact",
            Remediation = "DatabaseAgent must produce docker-compose.yml for local dev"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateServiceLayerAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "ServiceLayer";

        results.Add(CheckAgentCompleted(ctx, AgentType.ServiceLayer, agentName));

        var svcArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.ServiceLayer).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Service_ProducedArtifacts",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = svcArtifacts.Count > 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = svcArtifacts.Count > 0 ? $"{svcArtifacts.Count} service artifacts" : "No service artifacts",
            Remediation = "ServiceLayerAgent must execute after DatabaseAgent"
        });

        var hasDtos = svcArtifacts.Any(a => a.Layer == ArtifactLayer.Dto);
        results.Add(new TestDiagnostic
        {
            TestName = "Service_HasDTOs",
            AgentUnderTest = agentName,
            Category = "Content Validation",
            Outcome = hasDtos ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasDtos ? "DTOs generated" : "No DTO artifacts found",
            Remediation = "ServiceLayerAgent must generate record DTOs per entity"
        });

        var hasInterfaces = svcArtifacts.Any(a => a.Layer == ArtifactLayer.Service && a.FileName.StartsWith("I"));
        results.Add(new TestDiagnostic
        {
            TestName = "Service_HasInterfaces",
            AgentUnderTest = agentName,
            Category = "Content Validation",
            Outcome = hasInterfaces ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasInterfaces ? "Service interfaces present" : "No service interfaces (I*.cs)",
            Remediation = "ServiceLayerAgent must generate I<Service> interfaces for DI"
        });

        var hasKafkaEvents = svcArtifacts.Any(a => a.Content.Contains("IntegrationEvent") || a.Content.Contains("Kafka"));
        results.Add(new TestDiagnostic
        {
            TestName = "Service_HasKafkaIntegration",
            AgentUnderTest = agentName,
            Category = "Integration",
            Outcome = hasKafkaEvents ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasKafkaEvents ? "Kafka event publishing wired" : "No Kafka integration found in services",
            Remediation = "Services must publish domain events via Kafka producer"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateApplicationAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "Application";

        results.Add(CheckAgentCompleted(ctx, AgentType.Application, agentName));

        var appArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Application).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Application_ProducedArtifacts",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = appArtifacts.Count > 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = appArtifacts.Count > 0 ? $"{appArtifacts.Count} app artifacts" : "No application artifacts",
            Remediation = "ApplicationAgent must run after ServiceLayerAgent"
        });

        var hasGateway = appArtifacts.Any(a => a.Content.Contains("ReverseProxy") || a.Content.Contains("Yarp"));
        results.Add(new TestDiagnostic
        {
            TestName = "Application_HasApiGateway",
            AgentUnderTest = agentName,
            Category = "Architecture",
            Outcome = hasGateway ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasGateway ? "YARP API Gateway configured" : "No API Gateway found — services unreachable",
            Remediation = "ApplicationAgent must generate YARP reverse proxy configuration"
        });

        var hasEndpoints = appArtifacts.Any(a => a.Content.Contains("MapGet") || a.Content.Contains("MapPost"));
        results.Add(new TestDiagnostic
        {
            TestName = "Application_HasEndpoints",
            AgentUnderTest = agentName,
            Category = "Content Validation",
            Outcome = hasEndpoints ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasEndpoints ? "REST endpoints generated" : "No minimal API endpoints found",
            Remediation = "ApplicationAgent must map CRUD endpoints per entity"
        });

        var hasMiddleware = appArtifacts.Any(a => a.Content.Contains("TenantMiddleware") || a.Content.Contains("CorrelationId"));
        results.Add(new TestDiagnostic
        {
            TestName = "Application_HasMiddleware",
            AgentUnderTest = agentName,
            Category = "Cross-Cutting",
            Outcome = hasMiddleware ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasMiddleware ? "Tenant + CorrelationId middleware present" : "Missing cross-cutting middleware",
            Remediation = "ApplicationAgent must generate TenantMiddleware and CorrelationIdMiddleware"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateIntegrationAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "Integration";

        results.Add(CheckAgentCompleted(ctx, AgentType.Integration, agentName));

        var intArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Integration).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Integration_ProducedArtifacts",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = intArtifacts.Count > 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = intArtifacts.Count > 0 ? $"{intArtifacts.Count} integration artifacts" : "No integration artifacts",
            Remediation = "IntegrationAgent must produce Kafka consumers, outbox, FHIR/HL7 adapters"
        });

        var hasKafkaConsumer = intArtifacts.Any(a => a.Content.Contains("KafkaConsumer") || a.Content.Contains("BackgroundService"));
        results.Add(new TestDiagnostic
        {
            TestName = "Integration_HasKafkaConsumer",
            AgentUnderTest = agentName,
            Category = "Messaging",
            Outcome = hasKafkaConsumer ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasKafkaConsumer ? "Kafka consumer found" : "No Kafka consumer — events won't be processed",
            Remediation = "IntegrationAgent must generate KafkaConsumerHostedService"
        });

        var hasOutbox = intArtifacts.Any(a => a.Content.Contains("Outbox"));
        results.Add(new TestDiagnostic
        {
            TestName = "Integration_HasOutboxPattern",
            AgentUnderTest = agentName,
            Category = "Reliability",
            Outcome = hasOutbox ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasOutbox ? "Outbox pattern implemented" : "No outbox — at-least-once delivery not guaranteed",
            Remediation = "IntegrationAgent must generate OutboxProcessor with retry + DLQ"
        });

        var hasFhir = intArtifacts.Any(a => a.Content.Contains("FHIR") || a.Content.Contains("FhirAdapter"));
        results.Add(new TestDiagnostic
        {
            TestName = "Integration_HasFhirAdapter",
            AgentUnderTest = agentName,
            Category = "Interoperability",
            Outcome = hasFhir ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasFhir ? "FHIR R4 adapter present" : "No FHIR adapter — HL7 FHIR interop missing",
            Remediation = "IntegrationAgent must generate FHIR R4 resource adapters"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateTestingAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "Testing";

        results.Add(CheckAgentCompleted(ctx, AgentType.Testing, agentName));

        var testArtifacts = ctx.Artifacts.Where(a => a.ProducedBy == AgentType.Testing).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Testing_ProducedTests",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = testArtifacts.Count > 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = testArtifacts.Count > 0 ? $"{testArtifacts.Count} test artifacts" : "No test artifacts generated",
            Remediation = "TestingAgent must produce xUnit test files"
        });

        var hasTenantTests = testArtifacts.Any(a => a.Content.Contains("TenantIsolation"));
        results.Add(new TestDiagnostic
        {
            TestName = "Testing_HasSecurityTests",
            AgentUnderTest = agentName,
            Category = "Security Coverage",
            Outcome = hasTenantTests ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = hasTenantTests ? "Tenant isolation tests present" : "No tenant isolation tests",
            Remediation = "TestingAgent must generate multi-tenant security test stubs"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateReviewAgent(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();
        var agentName = "Review";

        results.Add(CheckAgentCompleted(ctx, AgentType.Review, agentName));

        results.Add(new TestDiagnostic
        {
            TestName = "Review_ProducedFindings",
            AgentUnderTest = agentName,
            Category = "Output Validation",
            Outcome = ctx.Findings.Count > 0 ? TestOutcome.Passed : TestOutcome.Skipped,
            Diagnostic = ctx.Findings.Count > 0
                ? $"{ctx.Findings.Count} review findings reported"
                : "No findings — either perfect code or review didn't run",
            Remediation = "ReviewAgent should always produce at least an info-level summary"
        });

        var blockingErrors = ctx.Findings.Count(f => f.Severity >= ReviewSeverity.Error);
        results.Add(new TestDiagnostic
        {
            TestName = "Review_NoBlockingErrors",
            AgentUnderTest = agentName,
            Category = "Quality Gate",
            Outcome = blockingErrors == 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = blockingErrors == 0 ? "No blocking errors" : $"{blockingErrors} blocking errors found",
            Remediation = "Fix blocking review errors before release — review findings detail specific issues"
        });

        return results;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Pipeline-level health checks
    // ═══════════════════════════════════════════════════════════════════

    private List<TestDiagnostic> ValidatePipelineIntegrity(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();

        // Check no duplicate artifact paths
        var paths = ctx.Artifacts.Select(a => a.RelativePath).ToList();
        var dupes = paths.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Pipeline_NoDuplicateArtifacts",
            AgentUnderTest = "Pipeline",
            Category = "Integrity",
            Outcome = dupes.Count == 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = dupes.Count == 0
                ? $"All {paths.Count} artifact paths unique"
                : $"{dupes.Count} duplicate paths: {string.Join(", ", dupes.Take(5))}",
            Remediation = "Agents producing overlapping files — check naming conventions"
        });

        // Check all artifacts have content
        var emptyArtifacts = ctx.Artifacts.Where(a => string.IsNullOrWhiteSpace(a.Content)).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Pipeline_NoEmptyArtifacts",
            AgentUnderTest = "Pipeline",
            Category = "Integrity",
            Outcome = emptyArtifacts.Count == 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = emptyArtifacts.Count == 0
                ? "All artifacts have content"
                : $"{emptyArtifacts.Count} empty artifacts: {string.Join(", ", emptyArtifacts.Take(3).Select(a => a.FileName))}",
            Remediation = "Agent produced empty file — check generator logic"
        });

        // Check every layer is represented
        var representedLayers = ctx.Artifacts.Select(a => a.Layer).Distinct().ToHashSet();
        var expectedLayers = new[] { ArtifactLayer.Database, ArtifactLayer.Repository, ArtifactLayer.Service,
            ArtifactLayer.Dto, ArtifactLayer.Integration, ArtifactLayer.Test, ArtifactLayer.Migration, ArtifactLayer.Configuration };
        var missingLayers = expectedLayers.Where(l => !representedLayers.Contains(l)).ToList();
        results.Add(new TestDiagnostic
        {
            TestName = "Pipeline_AllLayersCovered",
            AgentUnderTest = "Pipeline",
            Category = "Completeness",
            Outcome = missingLayers.Count == 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = missingLayers.Count == 0
                ? $"All {expectedLayers.Length} expected layers present"
                : $"Missing layers: {string.Join(", ", missingLayers)}",
            Remediation = "Check agents that own missing layers ran successfully"
        });

        return results;
    }

    private List<TestDiagnostic> ValidateNoStaleAgents(AgentContext ctx)
    {
        var results = new List<TestDiagnostic>();

        var staleAgents = ctx.AgentStatuses
            .Where(kv => kv.Key != AgentType.Orchestrator && kv.Key != AgentType.Supervisor)
            .Where(kv => kv.Value == AgentStatus.Running)
            .Select(kv => kv.Key)
            .ToList();

        results.Add(new TestDiagnostic
        {
            TestName = "Pipeline_NoStaleRunningAgents",
            AgentUnderTest = "Pipeline",
            Category = "Health",
            Outcome = staleAgents.Count == 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = staleAgents.Count == 0
                ? "No agents stuck in Running state"
                : $"Agents still marked Running (stalled?): {string.Join(", ", staleAgents)}",
            Remediation = "Force-complete or restart stale agents — check for deadlocks or infinite loops"
        });

        var failedAgents = ctx.AgentStatuses
            .Where(kv => kv.Key != AgentType.Orchestrator && kv.Key != AgentType.Supervisor)
            .Where(kv => kv.Value == AgentStatus.Failed)
            .Select(kv => kv.Key)
            .ToList();

        results.Add(new TestDiagnostic
        {
            TestName = "Pipeline_NoFailedAgents",
            AgentUnderTest = "Pipeline",
            Category = "Health",
            Outcome = failedAgents.Count == 0 ? TestOutcome.Passed : TestOutcome.Failed,
            Diagnostic = failedAgents.Count == 0
                ? "All agents completed successfully"
                : $"Failed agents: {string.Join(", ", failedAgents)}",
            Remediation = $"Re-run failed agents: {string.Join(", ", failedAgents)} — check logs for root cause"
        });

        return results;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Remediation — re-run failed agents
    // ═══════════════════════════════════════════════════════════════════

    private async Task<List<TestDiagnostic>> AttemptRemediationsAsync(
        List<TestDiagnostic> failures, AgentContext context, CancellationToken ct)
    {
        var remediations = new List<TestDiagnostic>();

        // Group failures by agent to determine which agents need re-run
        var agentsToRetry = failures
            .Where(f => f.AgentUnderTest != "Pipeline") // Pipeline-level issues can't be fixed by re-run
            .Select(f => f.AgentUnderTest)
            .Distinct()
            .ToList();

        foreach (var agentName in agentsToRetry)
        {
            if (!Enum.TryParse<AgentType>(agentName, out var agentType))
                continue;

            var attempt = context.RetryAttempts.GetValueOrDefault(agentType, 0) + 1;
            if (attempt > MaxRetries)
            {
                remediations.Add(new TestDiagnostic
                {
                    TestName = $"Remediation_{agentName}_MaxRetriesExceeded",
                    AgentUnderTest = agentName,
                    Category = "Remediation",
                    Outcome = TestOutcome.Failed,
                    Diagnostic = $"{agentName} failed after {MaxRetries} retry attempts",
                    Remediation = "Manual intervention required — check agent implementation",
                    AttemptNumber = attempt
                });
                continue;
            }

            context.RetryAttempts[agentType] = attempt;
            _logger.LogInformation("SupervisorAgent retrying {Agent} — attempt {Attempt}/{Max}",
                agentName, attempt, MaxRetries);

            var agent = GetOtherAgents().FirstOrDefault(a => a.Type == agentType);
            if (agent is null)
            {
                remediations.Add(new TestDiagnostic
                {
                    TestName = $"Remediation_{agentName}_AgentNotFound",
                    AgentUnderTest = agentName,
                    Category = "Remediation",
                    Outcome = TestOutcome.Failed,
                    Diagnostic = $"Cannot locate {agentName} agent for retry",
                    Remediation = "Agent not registered in DI container",
                    AttemptNumber = attempt
                });
                continue;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var result = await agent.ExecuteAsync(context, ct);
                sw.Stop();

                remediations.Add(new TestDiagnostic
                {
                    TestName = $"Remediation_{agentName}_Retry",
                    AgentUnderTest = agentName,
                    Category = "Remediation",
                    Outcome = result.Success ? TestOutcome.Remediated : TestOutcome.Failed,
                    Diagnostic = result.Success
                        ? $"{agentName} succeeded on retry attempt {attempt} ({sw.ElapsedMilliseconds}ms)"
                        : $"{agentName} still failing on attempt {attempt}: {string.Join("; ", result.Errors)}",
                    Remediation = result.Success
                        ? "Auto-remediated by re-running agent"
                        : "Agent continues to fail — escalate to manual review",
                    DurationMs = sw.ElapsedMilliseconds,
                    AttemptNumber = attempt
                });
            }
            catch (Exception ex)
            {
                remediations.Add(new TestDiagnostic
                {
                    TestName = $"Remediation_{agentName}_Exception",
                    AgentUnderTest = agentName,
                    Category = "Remediation",
                    Outcome = TestOutcome.Failed,
                    Diagnostic = $"{agentName} threw exception on retry: {ex.Message}",
                    Remediation = "Unhandled exception during remediation — needs code fix",
                    AttemptNumber = attempt
                });
            }
        }

        return remediations;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static TestDiagnostic CheckAgentCompleted(AgentContext ctx, AgentType type, string agentName)
    {
        var status = ctx.AgentStatuses.GetValueOrDefault(type, AgentStatus.Idle);
        return new TestDiagnostic
        {
            TestName = $"{agentName}_Completed",
            AgentUnderTest = agentName,
            Category = "Agent Health",
            Outcome = status == AgentStatus.Completed ? TestOutcome.Passed
                     : status == AgentStatus.Failed ? TestOutcome.Failed
                     : TestOutcome.Skipped,
            Diagnostic = status switch
            {
                AgentStatus.Completed => $"{agentName} completed successfully",
                AgentStatus.Failed => $"{agentName} failed — check agent logs",
                AgentStatus.Running => $"{agentName} still running (possible stall)",
                AgentStatus.Idle => $"{agentName} never started",
                _ => $"{agentName} in unexpected state: {status}"
            },
            Remediation = status == AgentStatus.Completed ? "N/A" : $"Retry {agentName} or check dependencies"
        };
    }
}
