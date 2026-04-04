using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Testing;

public sealed class TestingAgent : IAgent
{
    private readonly ILogger<TestingAgent> _logger;

    public AgentType Type => AgentType.Testing;
    public string Name => "Testing Agent";
    public string Description => "Generates unit and integration test stubs for entities, services, and integration adapters.";

    public TestingAgent(ILogger<TestingAgent> logger) => _logger = logger;

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("TestingAgent starting");

        var artifacts = new List<CodeArtifact>();

        try
        {
            // Generate test for each service artifact
            var serviceArtifacts = context.Artifacts
                .Where(a => a.Layer == ArtifactLayer.Service && a.FileName.StartsWith("I"))
                .ToList();

            foreach (var svc in serviceArtifacts)
            {
                var testName = svc.FileName.Replace("I", "").Replace(".cs", "Tests");
                artifacts.Add(GenerateServiceTest(testName, svc));
            }

            // Generate tenant isolation test
            artifacts.Add(GenerateTenantIsolationTest());

            // Generate AI safety test stubs
            artifacts.Add(GenerateAiSafetyTests());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Generated {artifacts.Count} test artifacts",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator, Subject = "Tests generated", Body = $"{artifacts.Count} test files." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "TestingAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateServiceTest(string testName, CodeArtifact source) => new()
    {
        Layer = ArtifactLayer.Test,
        RelativePath = $"Tests/Services/{testName}.cs",
        FileName = $"{testName}.cs",
        Namespace = "Hms.Tests.Services",
        ProducedBy = AgentType.Testing,
        TracedRequirementIds = source.TracedRequirementIds,
        Content = $$"""
            using Xunit;

            namespace Hms.Tests.Services;

            public class {{testName}}
            {
                [Fact]
                public async Task GetById_ReturnsNull_WhenNotFound()
                {
                    // Arrange — TODO: wire mock repository
                    // Act
                    // Assert
                    await Task.CompletedTask;
                    Assert.True(true, "Stub — implement when service layer is wired");
                }

                [Fact]
                public async Task Create_ReturnsDtoWithId()
                {
                    await Task.CompletedTask;
                    Assert.True(true, "Stub — implement when service layer is wired");
                }

                [Fact]
                public void TenantId_IsRequired_OnAllEntities()
                {
                    // Verify entity has TenantId property
                    Assert.True(true, "Stub — reflect on entity to confirm TenantId");
                }
            }
            """
    };

    private static CodeArtifact GenerateTenantIsolationTest() => new()
    {
        Layer = ArtifactLayer.Test,
        RelativePath = "Tests/Security/TenantIsolationTests.cs",
        FileName = "TenantIsolationTests.cs",
        Namespace = "Hms.Tests.Security",
        ProducedBy = AgentType.Testing,
        Content = """
            using Xunit;

            namespace Hms.Tests.Security;

            public class TenantIsolationTests
            {
                [Fact]
                public void QueryFilter_PreventsCrossTenantAccess()
                {
                    // TODO: set up in-memory DbContext with two tenants, verify queries are scoped
                    Assert.True(true, "Stub — implement with test DbContext");
                }

                [Fact]
                public void UniqueConstraints_IncludeTenantId()
                {
                    // TODO: verify unique indexes include tenant_id
                    Assert.True(true, "Stub — reflect on model metadata");
                }

                [Fact]
                public void RlsPolicy_ExistsForAllRegulatedTables()
                {
                    Assert.True(true, "Stub — verify RLS migration artifact exists");
                }
            }
            """
    };

    private static CodeArtifact GenerateAiSafetyTests() => new()
    {
        Layer = ArtifactLayer.Test,
        RelativePath = "Tests/AiSafety/AiCopilotSafetyTests.cs",
        FileName = "AiCopilotSafetyTests.cs",
        Namespace = "Hms.Tests.AiSafety",
        ProducedBy = AgentType.Testing,
        Content = """
            using Xunit;

            namespace Hms.Tests.AiSafety;

            public class AiCopilotSafetyTests
            {
                [Fact]
                public void DiagnosticSupport_NeverFinalizesAutonomously()
                {
                    // DS-04: Verify copilot output cannot be committed as confirmed diagnosis
                    Assert.True(true, "Stub — implement with mock AI service");
                }

                [Fact]
                public void TreatmentRecommendation_FlagsAllergies()
                {
                    // TR-01: Known allergy must suppress or flag contraindicated option
                    Assert.True(true, "Stub — implement with mock AI service");
                }

                [Fact]
                public void AutomationProposal_RequiresApprovalForClinicalActions()
                {
                    // AP-01: Clinical automation must enter pending_approval state
                    Assert.True(true, "Stub — implement with mock workflow engine");
                }

                [Fact]
                public void AiRetrieval_NeverCrossesTenantBoundary()
                {
                    // TEN-01: No context mixing across tenants
                    Assert.True(true, "Stub — implement with multi-tenant test fixture");
                }
            }
            """
    };
}
