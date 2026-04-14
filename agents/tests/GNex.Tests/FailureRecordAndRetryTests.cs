using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

/// <summary>
/// Tests for AgentFailureRecord storage in AgentContext, retry tracking,
/// and proper failure/success state transitions.
/// </summary>
public class FailureRecordAndRetryTests
{
    // ────────────────────────────────────────────────
    //  FailureRecords — ConcurrentBag operations
    // ────────────────────────────────────────────────

    [Fact]
    public void FailureRecords_InitiallyEmpty()
    {
        var ctx = new AgentContext();
        ctx.FailureRecords.Should().BeEmpty();
    }

    [Fact]
    public void FailureRecords_CanAddMultipleFailures()
    {
        var ctx = new AgentContext();

        ctx.FailureRecords.Add(new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Attempt = 1,
            Error = "Connection refused", Summary = "DB agent failed",
            ExceptionType = "Npgsql.NpgsqlException", NonRecoverable = false
        });
        ctx.FailureRecords.Add(new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Attempt = 2,
            Error = "Timeout", Summary = "DB agent failed again",
            ExceptionType = "TimeoutException", NonRecoverable = false
        });

        ctx.FailureRecords.Should().HaveCount(2);
        ctx.FailureRecords.Should().Contain(f => f.Attempt == 1);
        ctx.FailureRecords.Should().Contain(f => f.Attempt == 2);
    }

    [Fact]
    public void FailureRecords_ConcurrentSafety()
    {
        var ctx = new AgentContext();

        // Simulate concurrent failure recording from multiple agents
        Parallel.For(0, 100, i =>
        {
            ctx.FailureRecords.Add(new AgentFailureRecord
            {
                FailedAgent = (AgentType)(i % 10), Attempt = i,
                Error = $"Error {i}", Summary = $"Failure {i}"
            });
        });

        ctx.FailureRecords.Should().HaveCount(100);
    }

    [Fact]
    public void FailureRecord_StackTraceCapture()
    {
        var record = new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Attempt = 1,
            Error = "NullReferenceException",
            StackTrace = "   at DatabaseAgent.ExecuteAsync() in DatabaseAgent.cs:line 42",
            ExceptionType = "System.NullReferenceException",
            NonRecoverable = false
        };

        record.StackTrace.Should().Contain("DatabaseAgent.ExecuteAsync");
        record.ExceptionType.Should().Be("System.NullReferenceException");
    }

    [Fact]
    public void FailureRecord_NonRecoverableFlag()
    {
        var recoverable = new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Error = "Timeout", NonRecoverable = false
        };
        var nonRecoverable = new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Error = "Auth failed", NonRecoverable = true
        };

        recoverable.NonRecoverable.Should().BeFalse();
        nonRecoverable.NonRecoverable.Should().BeTrue();
    }

    // ────────────────────────────────────────────────
    //  RetryAttempts — tracking retries per agent
    // ────────────────────────────────────────────────

    [Fact]
    public void RetryAttempts_InitiallyEmpty()
    {
        var ctx = new AgentContext();
        ctx.RetryAttempts.Should().BeEmpty();
    }

    [Fact]
    public void RetryAttempts_IncrementPerAgent()
    {
        var ctx = new AgentContext();

        ctx.RetryAttempts.AddOrUpdate(AgentType.Database, 1, (_, old) => old + 1);
        ctx.RetryAttempts[AgentType.Database].Should().Be(1);

        ctx.RetryAttempts.AddOrUpdate(AgentType.Database, 1, (_, old) => old + 1);
        ctx.RetryAttempts[AgentType.Database].Should().Be(2);
    }

    [Fact]
    public void RetryAttempts_TrackMultipleAgents()
    {
        var ctx = new AgentContext();

        ctx.RetryAttempts[AgentType.Database] = 2;
        ctx.RetryAttempts[AgentType.Review] = 1;
        ctx.RetryAttempts[AgentType.Testing] = 0;

        ctx.RetryAttempts.Should().HaveCount(3);
        ctx.RetryAttempts[AgentType.Database].Should().Be(2);
        ctx.RetryAttempts[AgentType.Review].Should().Be(1);
    }

    // ────────────────────────────────────────────────
    //  AgentStatuses — state transitions
    // ────────────────────────────────────────────────

    [Fact]
    public void AgentStatuses_StateTransitions()
    {
        var ctx = new AgentContext();

        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Running;
        ctx.AgentStatuses[AgentType.Database].Should().Be(AgentStatus.Running);

        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Completed;
        ctx.AgentStatuses[AgentType.Database].Should().Be(AgentStatus.Completed);
    }

    [Fact]
    public void AgentStatuses_ConcurrentUpdatesFromMultipleAgents()
    {
        var ctx = new AgentContext();

        Parallel.ForEach(
            new[] { AgentType.Database, AgentType.Review, AgentType.Testing,
                    AgentType.Planning, AgentType.Architect },
            agentType =>
            {
                ctx.AgentStatuses[agentType] = AgentStatus.Running;
                Thread.Sleep(5); // simulate work
                ctx.AgentStatuses[agentType] = AgentStatus.Completed;
            });

        ctx.AgentStatuses.Values.Should().OnlyContain(s => s == AgentStatus.Completed);
    }

    [Fact]
    public void AgentStatuses_FailedAgentFilterable()
    {
        var ctx = new AgentContext();
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Failed;
        ctx.AgentStatuses[AgentType.Review] = AgentStatus.Completed;
        ctx.AgentStatuses[AgentType.Testing] = AgentStatus.Completed;
        ctx.AgentStatuses[AgentType.Planning] = AgentStatus.Failed;

        var failed = ctx.AgentStatuses
            .Where(kv => kv.Value == AgentStatus.Failed)
            .Select(kv => kv.Key)
            .ToList();

        failed.Should().HaveCount(2);
        failed.Should().Contain(AgentType.Database);
        failed.Should().Contain(AgentType.Planning);
    }

    // ────────────────────────────────────────────────
    //  AgentResults — cross-agent result inspection
    // ────────────────────────────────────────────────

    [Fact]
    public void AgentResults_StoreAndRetrieve()
    {
        var ctx = new AgentContext();
        var result = new AgentResult
        {
            Agent = AgentType.Architect,
            Success = true,
            Summary = "Architecture guidance published for 3 services"
        };

        ctx.AgentResults[AgentType.Architect] = result;

        ctx.AgentResults.Should().ContainKey(AgentType.Architect);
        ctx.AgentResults[AgentType.Architect].Summary.Should().Contain("3 services");
    }

    [Fact]
    public void AgentResults_TryGetValue_MissingAgent()
    {
        var ctx = new AgentContext();

        var found = ctx.AgentResults.TryGetValue(AgentType.Database, out var result);

        found.Should().BeFalse();
        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────
    //  Combined failure scenario
    // ────────────────────────────────────────────────

    [Fact]
    public void FullFailureScenario_StatusRetryAndRecord()
    {
        var ctx = new AgentContext();

        // Simulate agent failing twice then succeeding
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Running;

        // First failure
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Failed;
        ctx.RetryAttempts[AgentType.Database] = 1;
        ctx.FailureRecords.Add(new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Attempt = 1,
            Error = "Connection refused", NonRecoverable = false
        });

        // Second attempt — success
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Running;
        ctx.RetryAttempts.AddOrUpdate(AgentType.Database, 2, (_, _) => 2);
        ctx.AgentStatuses[AgentType.Database] = AgentStatus.Completed;
        ctx.AgentResults[AgentType.Database] = new AgentResult
        {
            Agent = AgentType.Database, Success = true,
            Summary = "Generated 5 DB artifacts"
        };

        // Verify final state
        ctx.AgentStatuses[AgentType.Database].Should().Be(AgentStatus.Completed);
        ctx.RetryAttempts[AgentType.Database].Should().Be(2);
        ctx.FailureRecords.Should().HaveCount(1, "only the actual failure is recorded");
        ctx.AgentResults[AgentType.Database].Success.Should().BeTrue();
    }

    // ────────────────────────────────────────────────
    //  FailureRecord defaults
    // ────────────────────────────────────────────────

    [Fact]
    public void FailureRecord_Defaults()
    {
        var record = new AgentFailureRecord();

        record.Error.Should().BeEmpty();
        record.Summary.Should().BeEmpty();
        record.ExceptionType.Should().BeEmpty();
        record.StackTrace.Should().BeEmpty();
        record.NonRecoverable.Should().BeFalse();
        record.Remediated.Should().BeFalse();
        record.FailedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FailureRecord_RemediatedFlag()
    {
        var record = new AgentFailureRecord
        {
            FailedAgent = AgentType.Database, Error = "DDL error"
        };
        record.Remediated.Should().BeFalse();

        record.Remediated = true;
        record.Remediated.Should().BeTrue();
    }
}
