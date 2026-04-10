using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;
using System.Reflection;

namespace GNex.Tests;

/// <summary>
/// Tests that the orchestrator assigns the correct agents to tasks based on
/// Title, Description, TechnicalNotes, DefinitionOfDone, and AcceptanceCriteria.
/// </summary>
public class AgentAssignmentTests
{
    // Use reflection to call the private static GetRelevantTaskAgents method
    private static readonly MethodInfo s_method = typeof(GNex.Agents.Orchestrator.AgentOrchestrator)
        .GetMethod("GetRelevantTaskAgents", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<string> GetRelevantTaskAgents(ExpandedRequirement item)
        => (List<string>)s_method.Invoke(null, [item])!;

    // ── Assignment from Description text ──

    [Fact]
    public void DatabaseTask_AssignsDatabaseAgent()
    {
        var item = MakeTask("Create patient schema", "Design database schema for PatientProfile with RLS policies");
        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Database.ToString());
    }

    [Fact]
    public void ServiceTask_AssignsServiceLayerAgent()
    {
        var item = MakeTask("Implement patient service", "Create business logic service with validation for patient CRUD");
        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.ServiceLayer.ToString());
    }

    [Fact]
    public void ApiTask_AssignsApplicationAgent()
    {
        var item = MakeTask("Create patient API endpoints", "Build REST API endpoints for patient management");
        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Application.ToString());
    }

    [Fact]
    public void IntegrationTask_AssignsIntegrationAgent()
    {
        var item = MakeTask("Add HL7v2 integration", "Implement HL7v2 message processing with FHIR adapter");
        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Integration.ToString());
    }

    [Fact]
    public void TestingTask_AssignsTestingAgent()
    {
        var item = MakeTask("Write unit tests", "Create xUnit test cases with Moq for PatientService");
        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Testing.ToString());
    }

    [Fact]
    public void SecurityTask_AssignsSecurityAgent()
    {
        var item = MakeTask("Implement RBAC", "Add authentication and authorization for API endpoints");
        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Security.ToString());
    }

    // ── Assignment from DefinitionOfDone ──

    [Theory]
    [InlineData("Unit tests written and passing", "Testing")]
    [InlineData("Database schema created with proper indexes", "Database")]
    [InlineData("API endpoint returns correct HTTP codes", "Application")]
    [InlineData("Kafka integration events published", "Integration")]
    [InlineData("Service layer validation implemented", "ServiceLayer")]
    [InlineData("HIPAA compliance verified", "HipaaCompliance")]
    [InlineData("Health check endpoint responds 200", "Observability")]
    [InlineData("Docker infrastructure configured", "Infrastructure")]
    public void DodEntry_AssignsCorrectAgent(string dodItem, string expectedAgent)
    {
        var item = MakeTask("Generic task", "Implement feature X");
        item.DefinitionOfDone.Add(dodItem);

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(expectedAgent);
    }

    // ── Assignment from AcceptanceCriteria ──

    [Fact]
    public void AcceptanceCriteria_InfluenceAssignment()
    {
        var item = MakeTask("User story implementation", "As a nurse, I can view patient records");
        item.AcceptanceCriteria.AddRange(
        [
            "Patient API endpoint returns data within 200ms",
            "Unit tests cover all CRUD operations"
        ]);

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Application.ToString(), "AC mentions 'API endpoint'");
        agents.Should().Contain(AgentType.Testing.ToString(), "AC mentions 'unit tests'");
    }

    // ── Multi-agent tasks from DOD ──

    [Fact]
    public void FullStackTask_AssignsMultipleAgents()
    {
        var item = MakeTask("Implement encounter module", "Full encounter management with database, service, and API");
        item.DefinitionOfDone.AddRange(
        [
            "Database tables created for encounters",
            "Service layer implements encounter business logic",
            "API endpoints return proper responses",
            "Unit tests pass with 80% coverage",
            "Kafka events published for state changes"
        ]);

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Database.ToString());
        agents.Should().Contain(AgentType.ServiceLayer.ToString());
        agents.Should().Contain(AgentType.Application.ToString());
        agents.Should().Contain(AgentType.Testing.ToString());
        agents.Should().Contain(AgentType.Integration.ToString());
    }

    // ── Assignment from Tags ──

    [Fact]
    public void Tag_OverridesWhenPresent()
    {
        var item = MakeTask("Some task", "No keywords here");
        item.Tags.Add("security");

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Security.ToString());
    }

    // ── Assignment from AffectedServices ──

    [Fact]
    public void AffectedServices_AssignsCodeGenAgents()
    {
        var item = MakeTask("Implement feature", "Some general feature");
        item.AffectedServices.AddRange(["PatientService", "EncounterService"]);

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.Database.ToString());
        agents.Should().Contain(AgentType.ServiceLayer.ToString());
        agents.Should().Contain(AgentType.Application.ToString());
    }

    // ── Bug items always include BugFix ──

    [Fact]
    public void BugItem_AlwaysIncludesBugFix()
    {
        var item = new ExpandedRequirement
        {
            Id = "BUG-001",
            ItemType = WorkItemType.Bug,
            Title = "Patient search returns wrong results",
            Description = "The API endpoint returns stale data"
        };

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.BugFix.ToString());
    }

    // ── Fallback ──

    [Fact]
    public void EmptyTask_FallsBackToServiceLayer()
    {
        var item = MakeTask("Something", "");

        var agents = GetRelevantTaskAgents(item);
        agents.Should().NotBeEmpty("should never return empty list");
    }

    // ── UI/UX from DOD ──

    [Fact]
    public void DashboardDod_AssignsUiUxAgent()
    {
        var item = MakeTask("Create admin dashboard", "Implement dashboard for hospital admin");
        item.DefinitionOfDone.Add("Dashboard page displays patient statistics");

        var agents = GetRelevantTaskAgents(item);
        agents.Should().Contain(AgentType.UiUx.ToString());
    }

    // ── Helpers ──

    private static ExpandedRequirement MakeTask(string title, string description) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..8],
        ItemType = WorkItemType.Task,
        Status = WorkItemStatus.InQueue,
        Title = title,
        Description = description,
        Tags = [],
        DefinitionOfDone = [],
        AcceptanceCriteria = []
    };
}
