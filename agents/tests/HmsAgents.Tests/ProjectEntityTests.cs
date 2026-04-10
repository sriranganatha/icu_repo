using FluentAssertions;
using Hms.Database.Entities.Platform.Projects;

namespace HmsAgents.Tests;

/// <summary>
/// Tests for Phase 2 project-specific entities — creation, defaults, tech stack, backlog hierarchy.
/// </summary>
public class ProjectEntityTests
{
    [Fact]
    public void Project_DefaultStatus()
    {
        var project = new Project { Name = "TestProject", Slug = "test-project" };
        project.Status.Should().Be("draft");
        project.ProjectType.Should().Be("web_app");
    }

    [Fact]
    public void Project_NavigationCollections_Empty()
    {
        var project = new Project { Name = "Test", Slug = "test" };
        project.TechStack.Should().BeEmpty();
        project.Epics.Should().BeEmpty();
        project.Sprints.Should().BeEmpty();
        project.Modules.Should().BeEmpty();
        project.AgentAssignments.Should().BeEmpty();
        project.Metrics.Should().BeEmpty();
    }

    [Fact]
    public void ProjectTechStack_LayerAndType()
    {
        var stack = new ProjectTechStack
        {
            ProjectId = "proj1",
            Layer = "backend",
            TechnologyType = "framework",
            TechnologyId = "tech1",
            Version = "9.0"
        };
        stack.Layer.Should().Be("backend");
        stack.TechnologyType.Should().Be("framework");
    }

    [Fact]
    public void ProjectSettings_DefaultBranch()
    {
        var settings = new ProjectSettings
        {
            ProjectId = "proj1",
            GitRepoUrl = "https://github.com/org/repo",
            DefaultBranch = "main"
        };
        settings.DefaultBranch.Should().Be("main");
    }

    [Fact]
    public void EnvironmentConfig_EnvNameAndVariables()
    {
        var env = new EnvironmentConfig
        {
            ProjectId = "proj1",
            EnvName = "production",
            VariablesJson = "{\"API_URL\":\"https://api.example.com\"}"
        };
        env.EnvName.Should().Be("production");
        env.VariablesJson.Should().Contain("API_URL");
    }

    [Fact]
    public void Epic_StoriesCollection()
    {
        var epic = new Epic
        {
            ProjectId = "proj1",
            Title = "User Management",
            Priority = "high"
        };
        epic.Stories.Should().BeEmpty();
        epic.Priority.Should().Be("high");
    }

    [Fact]
    public void Story_TasksCollection()
    {
        var story = new Story
        {
            EpicId = "epic1",
            Title = "Login Page",
            StoryPoints = 5
        };
        story.Tasks.Should().BeEmpty();
        story.StoryPoints.Should().Be(5);
    }

    [Fact]
    public void TaskItem_DefaultStatus()
    {
        var task = new TaskItem
        {
            StoryId = "story1",
            TaskType = "code"
        };
        task.Status.Should().Be("pending");
    }

    [Fact]
    public void Sprint_DateRange()
    {
        var start = DateTimeOffset.UtcNow;
        var sprint = new Sprint
        {
            ProjectId = "proj1",
            Name = "Sprint 1",
            StartDate = start,
            EndDate = start.AddDays(14)
        };
        sprint.EndDate.Should().NotBeNull();
        sprint.EndDate!.Value.Should().BeAfter(sprint.StartDate!.Value);
    }

    [Fact]
    public void AgentAssignment_DefaultStatus()
    {
        var assignment = new AgentAssignment
        {
            TaskId = "task1",
            AgentTypeDefinitionId = "def1"
        };
        assignment.Status.Should().Be("pending");
        assignment.StartedAt.Should().BeNull();
        assignment.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void ProjectArchitecture_PatternId()
    {
        var arch = new ProjectArchitecture
        {
            ProjectId = "proj1",
            PatternId = "microservices"
        };
        arch.PatternId.Should().Be("microservices");
    }

    [Fact]
    public void ModuleDefinition_BasicProperties()
    {
        var module = new ModuleDefinition
        {
            ProjectId = "proj1",
            Name = "PatientService",
            Description = "Patient service module",
            DependenciesJson = "[\"CoreModule\"]"
        };
        module.Description.Should().Be("Patient service module");
        module.DependenciesJson.Should().Contain("CoreModule");
    }

    [Fact]
    public void RawRequirement_SourceTracking()
    {
        var req = new RawRequirement
        {
            ProjectId = "proj1",
            InputText = "As an admin, I want multi-tenant support",
            InputType = "text"
        };
        req.InputType.Should().Be("text");
    }

    [Fact]
    public void ProjectMetric_ValueAndDate()
    {
        var metric = new ProjectMetric
        {
            ProjectId = "proj1",
            MetricType = "velocity",
            Value = 87.5m,
            RecordedAt = DateTimeOffset.UtcNow
        };
        metric.Value.Should().Be(87.5m);
    }

    [Fact]
    public void QualityReport_SummaryDefault()
    {
        var report = new QualityReport
        {
            ProjectId = "proj1",
            DetailsJson = "{\"type\":\"code_review\"}"
        };
        report.DetailsJson.Should().Contain("code_review");
    }
}
