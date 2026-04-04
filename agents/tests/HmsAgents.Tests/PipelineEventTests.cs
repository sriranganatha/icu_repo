using FluentAssertions;
using HmsAgents.Agents.Orchestrator;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;

namespace HmsAgents.Tests;

public class PipelineEventTests
{
    [Fact]
    public void PipelineEvent_InitializesWithTimestamp()
    {
        var evt = new PipelineEvent { RunId = "test", Agent = AgentType.Database, Status = AgentStatus.Running };
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PipelineEvent_CanIncludeTestDiagnostics()
    {
        var evt = new PipelineEvent
        {
            RunId = "test",
            Agent = AgentType.Supervisor,
            Status = AgentStatus.Completed,
            TestDiagnostics =
            [
                new TestDiagnosticDto
                {
                    TestName = "DB_Check",
                    AgentUnderTest = "Database",
                    Outcome = 0,
                    Diagnostic = "All good",
                    Remediation = "N/A"
                }
            ]
        };

        evt.TestDiagnostics.Should().HaveCount(1);
        evt.TestDiagnostics![0].Outcome.Should().Be(0);
    }

    [Fact]
    public void PipelineEvent_RetryAttemptDefaultsToZero()
    {
        var evt = new PipelineEvent();
        evt.RetryAttempt.Should().Be(0);
    }

    [Fact]
    public void TestDiagnosticDto_SerializesAllFields()
    {
        var dto = new TestDiagnosticDto
        {
            TestName = "Test1",
            AgentUnderTest = "Database",
            Outcome = 1,
            Diagnostic = "Failed to connect",
            Remediation = "Check connection string",
            Category = "Health",
            DurationMs = 123.45,
            AttemptNumber = 2
        };

        dto.TestName.Should().Be("Test1");
        dto.DurationMs.Should().BeApproximately(123.45, 0.01);
        dto.AttemptNumber.Should().Be(2);
    }
}
