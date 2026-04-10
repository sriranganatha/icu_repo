using FluentAssertions;
using GNex.Agents.Application;
using GNex.Agents.Database;
using GNex.Agents.Integration;
using GNex.Agents.Service;
using GNex.Agents.Testing;
using GNex.Agents.BugFix;
using GNex.Agents.DodVerification;
using GNex.Core.Enums;
using GNex.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests;

/// <summary>
/// Tests that all code-gen agents:
///   1. Accept claimed work items via context.CurrentClaimedItems
///   2. Generate artifacts based on the task
///   3. Complete their claimed items via context.CompleteWorkItem delegate
///   4. Return success with produced artifacts
/// </summary>
public class AgentBacklogExecutionTests
{
    // ── DatabaseAgent ──

    [Fact]
    public async Task DatabaseAgent_CompletesClaimedItems()
    {
        var agent = new DatabaseAgent(new Mock<ILogger<DatabaseAgent>>().Object);
        var (context, completed) = CreateContext(
            MakeDbTask("Create patient_profile schema", "Database schema for patient profiles with RLS"),
            MakeDbTask("Create encounter tables", "Database tables for encounter management"));

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().NotBeEmpty("should generate database artifacts");
        completed.Should().HaveCountGreaterOrEqualTo(1, "agent should complete claimed items");
    }

    [Fact]
    public async Task DatabaseAgent_GeneratesArtifactsForMicroservices()
    {
        var agent = new DatabaseAgent(new Mock<ILogger<DatabaseAgent>>().Object);
        var (context, _) = CreateContext(
            MakeDbTask("Implement patient DB layer", "Schema, entities, DbContext for patient service"));

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().Contain(a => a.FileName.EndsWith("DbContext.cs"), "should generate DbContext");
    }

    // ── ServiceLayerAgent ──

    [Fact]
    public async Task ServiceLayerAgent_CompletesClaimedItems()
    {
        var agent = new ServiceLayerAgent(new Mock<ILogger<ServiceLayerAgent>>().Object);
        var (context, completed) = CreateContext(
            MakeServiceTask("Implement patient service", "Business logic for patient CRUD with validation"));

        // ServiceLayerAgent needs domain model from DB artifacts
        context.DomainModel = new ParsedDomainModel();

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().NotBeEmpty("should generate service layer artifacts");
        completed.Should().NotBeEmpty("agent should complete claimed items");
    }

    [Fact]
    public async Task ServiceLayerAgent_GeneratesDtosAndServices()
    {
        var agent = new ServiceLayerAgent(new Mock<ILogger<ServiceLayerAgent>>().Object);
        var (context, _) = CreateContext(
            MakeServiceTask("Patient service CRUD", "Create DTOs, service interfaces and implementations"));

        context.DomainModel = new ParsedDomainModel();

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().Contain(a => a.FileName.Contains("Dto"), "should generate DTOs");
        result.Artifacts.Should().Contain(a => a.FileName.StartsWith("I") && a.FileName.Contains("Service"), "should generate service interfaces");
    }

    // ── ApplicationAgent ──

    [Fact]
    public async Task ApplicationAgent_CompletesClaimedItems()
    {
        var agent = new ApplicationAgent(new Mock<ILogger<ApplicationAgent>>().Object);
        var (context, completed) = CreateContext(
            MakeApiTask("Create patient API endpoints", "REST endpoints for patient CRUD"));

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().NotBeEmpty("should generate application artifacts");
        completed.Should().NotBeEmpty("agent should complete claimed items");
    }

    [Fact]
    public async Task ApplicationAgent_GeneratesGatewayAndServiceApis()
    {
        var agent = new ApplicationAgent(new Mock<ILogger<ApplicationAgent>>().Object);
        var (context, _) = CreateContext(
            MakeApiTask("Build API gateway", "YARP gateway + per-service APIs"));

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().Contain(a => a.FileName.Contains("Gateway") || a.RelativePath.Contains("Gateway"),
            "should generate API gateway");
    }

    // ── IntegrationAgent ──

    [Fact]
    public async Task IntegrationAgent_CompletesClaimedItems()
    {
        var agent = new IntegrationAgent(new Mock<ILogger<IntegrationAgent>>().Object);
        var (context, completed) = CreateContext(
            MakeIntegrationTask("Add Kafka event bus", "Kafka consumers, producers, outbox pattern"));

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().NotBeEmpty("should generate integration artifacts");
        completed.Should().NotBeEmpty("agent should complete claimed items");
    }

    [Fact]
    public async Task IntegrationAgent_GeneratesKafkaAndFhirArtifacts()
    {
        var agent = new IntegrationAgent(new Mock<ILogger<IntegrationAgent>>().Object);
        var (context, _) = CreateContext(
            MakeIntegrationTask("HL7/FHIR integration", "FHIR R4 adapter and HL7v2 processor"));

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().Contain(a =>
            a.FileName.Contains("Fhir") || a.FileName.Contains("Hl7") || a.FileName.Contains("Kafka"),
            "should generate FHIR/HL7/Kafka artifacts");
    }

    // ── TestingAgent ──

    [Fact]
    public async Task TestingAgent_CompletesClaimedItems()
    {
        var agent = new TestingAgent(new Mock<ILogger<TestingAgent>>().Object);
        var (context, completed) = CreateContext(
            MakeTestTask("Write unit tests for PatientService", "xUnit tests with Moq for all CRUD operations"));

        context.DomainModel = new ParsedDomainModel();

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Artifacts.Should().NotBeEmpty("should generate test artifacts");
        completed.Should().NotBeEmpty("agent should complete claimed items");
    }

    // ── BugFixAgent ──

    [Fact]
    public async Task BugFixAgent_CompletesClaimedItems()
    {
        var agent = new BugFixAgent(new Mock<ILogger<BugFixAgent>>().Object);
        var (context, completed) = CreateContext(
            MakeBugTask("Fix missing TenantId", "Entity PatientProfile missing TenantId column"));

        // BugFixAgent needs findings and artifacts to work on
        context.Artifacts.Add(new CodeArtifact
        {
            Id = "art-1",
            Layer = ArtifactLayer.Database,
            FileName = "PatientProfile.cs",
            RelativePath = "Patient/PatientProfile.cs",
            Content = "public class PatientProfile { public Guid Id {get;set;} // TODO: add TenantId }",
            ProducedBy = AgentType.Database
        });
        context.Findings.Add(new ReviewFinding
        {
            Id = "find-1",
            ArtifactId = "art-1",
            Category = "NFR-CODE-01",
            Severity = ReviewSeverity.Warning,
            Message = "TODO comment in PatientProfile.cs"
        });

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        completed.Should().NotBeEmpty("agent should complete claimed items");
    }

    // ── Multiple items test ──

    [Fact]
    public async Task Agent_CompletesAllClaimedItems_NotJustFirst()
    {
        var agent = new ServiceLayerAgent(new Mock<ILogger<ServiceLayerAgent>>().Object);
        var items = Enumerable.Range(1, 5)
            .Select(i => MakeServiceTask($"Service task {i}", $"Implement service for module {i}"))
            .ToArray();
        var (context, completed) = CreateContext(items);
        context.DomainModel = new ParsedDomainModel();

        var result = await agent.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        completed.Should().HaveCount(5, "agent should complete ALL 5 claimed items, not just some");
    }

    // ── Agent returns artifacts in result ──

    [Fact]
    public async Task Agent_ReturnsArtifactsInResult()
    {
        var agent = new DatabaseAgent(new Mock<ILogger<DatabaseAgent>>().Object);
        var (context, _) = CreateContext(
            MakeDbTask("Create schemas", "All database schemas for the system"));

        var result = await agent.ExecuteAsync(context);

        result.Artifacts.Should().NotBeEmpty();
        result.Artifacts.Should().AllSatisfy(a =>
        {
            a.Content.Should().NotBeNullOrWhiteSpace("each artifact should have content");
            a.FileName.Should().NotBeNullOrWhiteSpace("each artifact should have a filename");
        });
    }

    // ── Helpers ──

    private static (AgentContext context, List<ExpandedRequirement> completed) CreateContext(
        params ExpandedRequirement[] claimedItems)
    {
        var completed = new List<ExpandedRequirement>();
        var context = new AgentContext
        {
            PipelineConfig = new PipelineConfig
            {
                OutputPath = "/tmp/test-output",
                ExecuteDdl = false,
                SpinUpDocker = false
            },
            CurrentClaimedItems = claimedItems.ToList(),
            CompleteWorkItem = item => completed.Add(item),
            FailWorkItem = (item, reason) => { /* no-op for tests */ }
        };

        // Add claimed items to expanded requirements
        foreach (var item in claimedItems)
            context.ExpandedRequirements.Add(item);

        return (context, completed);
    }

    private static ExpandedRequirement MakeDbTask(string title, string description) =>
        MakeTask(title, description, ["database"], ["Database schema created", "Entity classes generated"]);

    private static ExpandedRequirement MakeServiceTask(string title, string description) =>
        MakeTask(title, description, ["service"], ["Service interface defined", "Service implementation complete"]);

    private static ExpandedRequirement MakeApiTask(string title, string description) =>
        MakeTask(title, description, ["api"], ["API endpoint returns 200", "Swagger documentation generated"]);

    private static ExpandedRequirement MakeIntegrationTask(string title, string description) =>
        MakeTask(title, description, ["integration"], ["Kafka events published", "FHIR adapter functional"]);

    private static ExpandedRequirement MakeTestTask(string title, string description) =>
        MakeTask(title, description, ["testing"], ["Unit tests pass", "Coverage above threshold"]);

    private static ExpandedRequirement MakeBugTask(string title, string description) => new()
    {
        Id = $"BUG-{Guid.NewGuid():N}"[..12],
        ItemType = WorkItemType.Bug,
        Status = WorkItemStatus.InProgress,
        Title = title,
        Description = description,
        Tags = ["bugfix"]
    };

    private static ExpandedRequirement MakeTask(string title, string description, List<string> tags, List<string> dod) => new()
    {
        Id = $"TASK-{Guid.NewGuid():N}"[..12],
        ItemType = WorkItemType.Task,
        Status = WorkItemStatus.InProgress,
        Title = title,
        Description = description,
        Tags = tags,
        DefinitionOfDone = dod,
        AcceptanceCriteria = [$"{title} is functional and tested"]
    };
}
