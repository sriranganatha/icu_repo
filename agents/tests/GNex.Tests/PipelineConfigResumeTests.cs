using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests;

/// <summary>
/// Tests for PipelineConfig resume properties: ResumeRequirements,
/// ResumeExpandedRequirements, ResumeDerivedServices, and ResumeCompletedAgents.
/// Validates the data contract for the resume pipeline flow.
/// </summary>
public class PipelineConfigResumeTests
{
    // ────────────────────────────────────────────────
    //  Defaults — all null
    // ────────────────────────────────────────────────

    [Fact]
    public void PipelineConfig_ResumeFields_DefaultNull()
    {
        var config = new PipelineConfig();

        config.ResumeRequirements.Should().BeNull();
        config.ResumeExpandedRequirements.Should().BeNull();
        config.ResumeDerivedServices.Should().BeNull();
        config.ResumeCompletedAgents.Should().BeNull();
    }

    // ────────────────────────────────────────────────
    //  ResumeRequirements
    // ────────────────────────────────────────────────

    [Fact]
    public void ResumeRequirements_CanSetAndRead()
    {
        var config = new PipelineConfig
        {
            ResumeRequirements =
            [
                new Requirement { Id = "REQ-001", Title = "User auth", Description = "Authentication" },
                new Requirement { Id = "REQ-002", Title = "User roles", Description = "RBAC" }
            ]
        };

        config.ResumeRequirements.Should().HaveCount(2);
        config.ResumeRequirements![0].Title.Should().Be("User auth");
    }

    [Fact]
    public void ResumeRequirements_EmptyList_DifferentFromNull()
    {
        var config = new PipelineConfig { ResumeRequirements = [] };

        config.ResumeRequirements.Should().NotBeNull();
        config.ResumeRequirements.Should().BeEmpty();

        // This matters: the resume code checks `is { Count: > 0 }`, so empty list != populated
        (config.ResumeRequirements is { Count: > 0 }).Should().BeFalse();
    }

    // ────────────────────────────────────────────────
    //  ResumeDerivedServices
    // ────────────────────────────────────────────────

    [Fact]
    public void ResumeDerivedServices_CanSetAndRead()
    {
        var config = new PipelineConfig
        {
            ResumeDerivedServices =
            [
                new MicroserviceDefinition
                {
                    Name = "Patient", ShortName = "pat", Schema = "patient_schema",
                    Description = "Patient service", ApiPort = 5200,
                    Entities = ["Patient", "Diagnosis"], DependsOn = []
                }
            ]
        };

        config.ResumeDerivedServices.Should().HaveCount(1);
        config.ResumeDerivedServices![0].Entities.Should().Contain("Patient");
    }

    [Fact]
    public void ResumeDerivedServices_EmptyList_DifferentFromNull()
    {
        var nullServices = new PipelineConfig { ResumeDerivedServices = null };
        var emptyServices = new PipelineConfig { ResumeDerivedServices = [] };

        nullServices.ResumeDerivedServices.Should().BeNull();
        emptyServices.ResumeDerivedServices.Should().NotBeNull().And.BeEmpty();
    }

    // ────────────────────────────────────────────────
    //  ResumeCompletedAgents
    // ────────────────────────────────────────────────

    [Fact]
    public void ResumeCompletedAgents_CanSetHashSet()
    {
        var config = new PipelineConfig
        {
            ResumeCompletedAgents = new HashSet<string>
            {
                "RequirementsReader", "RequirementsExpander", "Architect"
            }
        };

        config.ResumeCompletedAgents.Should().HaveCount(3);
        config.ResumeCompletedAgents.Should().Contain("RequirementsReader");
    }

    [Fact]
    public void ResumeCompletedAgents_ContainsCheck_CaseSensitive()
    {
        var config = new PipelineConfig
        {
            ResumeCompletedAgents = new HashSet<string> { "RequirementsReader" }
        };

        config.ResumeCompletedAgents.Should().Contain("RequirementsReader");
        // Default HashSet is case-sensitive
        config.ResumeCompletedAgents.Contains("requirementsreader").Should().BeFalse();
    }

    [Fact]
    public void ResumeCompletedAgents_EmptySet_RunsAll()
    {
        var config = new PipelineConfig { ResumeCompletedAgents = [] };

        // When empty, no agents should be skipped
        config.ResumeCompletedAgents.Should().BeEmpty();
        config.ResumeCompletedAgents.Contains("RequirementsReader").Should().BeFalse();
    }

    // ────────────────────────────────────────────────
    //  ResumeExpandedRequirements
    // ────────────────────────────────────────────────

    [Fact]
    public void ResumeExpandedRequirements_CanSetAndRead()
    {
        var config = new PipelineConfig
        {
            ResumeExpandedRequirements =
            [
                new ExpandedRequirement
                {
                    Id = "WI-001", Title = "Create patient table",
                    ItemType = WorkItemType.Task, Status = WorkItemStatus.InProgress,
                    Module = "Patient", Priority = 1, Iteration = 1,
                    AssignedAgent = "Database"
                }
            ]
        };

        config.ResumeExpandedRequirements.Should().HaveCount(1);
        config.ResumeExpandedRequirements![0].AssignedAgent.Should().Be("Database");
    }

    // ────────────────────────────────────────────────
    //  Combined resume scenario
    // ────────────────────────────────────────────────

    [Fact]
    public void FullResumeConfig_AllFieldsPopulated()
    {
        var config = new PipelineConfig
        {
            ResumeCompletedAgents = new HashSet<string>
                { "RequirementsReader", "RequirementsExpander", "Architect" },
            ResumeRequirements =
            [
                new Requirement { Id = "REQ-001", Title = "Auth", Description = "Authentication" }
            ],
            ResumeExpandedRequirements =
            [
                new ExpandedRequirement
                {
                    Id = "WI-001", Title = "Implement login", ItemType = WorkItemType.UserStory,
                    Status = WorkItemStatus.New, Priority = 1, Iteration = 1
                }
            ],
            ResumeDerivedServices =
            [
                new MicroserviceDefinition
                {
                    Name = "Auth", ShortName = "auth", Schema = "auth_schema",
                    Description = "Auth service", ApiPort = 5200,
                    Entities = ["User", "Role"], DependsOn = []
                }
            ]
        };

        config.ResumeCompletedAgents.Should().HaveCount(3);
        config.ResumeRequirements.Should().HaveCount(1);
        config.ResumeExpandedRequirements.Should().HaveCount(1);
        config.ResumeDerivedServices.Should().HaveCount(1);
    }

    // ────────────────────────────────────────────────
    //  Pattern check: is { Count: > 0 } — used in resume
    // ────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void PatternCheck_IsCountGreaterThanZero(int? count, bool expected)
    {
        List<Requirement>? list = count switch
        {
            null => null,
            0 => [],
            _ => Enumerable.Range(1, count.Value)
                .Select(i => new Requirement { Id = $"R-{i}", Title = $"Req {i}" })
                .ToList()
        };

        var result = list is { Count: > 0 };
        result.Should().Be(expected);
    }

    // ────────────────────────────────────────────────
    //  Docker/DDL config — prevents accidental execution in tests
    // ────────────────────────────────────────────────

    [Fact]
    public void PipelineConfig_DefaultsDockerAndDdlEnabled()
    {
        var config = new PipelineConfig();

        // By default both are true (opt-out pattern)
        config.SpinUpDocker.Should().BeTrue();
        config.ExecuteDdl.Should().BeTrue();
    }
}
