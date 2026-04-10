using FluentAssertions;
using GNex.Agents.Requirements;
using GNex.Core.Enums;
using GNex.Core.Interfaces;
using GNex.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GNex.Tests;

public class RequirementsReaderTests
{
    [Fact]
    public async Task ReadAll_FindsMarkdownFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gnex-reqs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "epic-patient.md"), @"# Patient Management
## REQ-PAT-001 Patient Registration
The system shall support patient registration with demographics.
### Tags
Patient, Registration
");
        File.WriteAllText(Path.Combine(tempDir, "epic-encounter.md"), @"# Encounter Management
## REQ-ENC-001 Encounter Creation
The system shall support creating encounters.
### Tags
Encounter
");

        try
        {
            var parser = new RequirementParser(new Mock<ILogger<RequirementParser>>().Object, new Mock<IServiceScopeFactory>().Object);

            // Act
            var requirements = await parser.ReadAllAsync(tempDir);

            // Assert
            requirements.Should().NotBeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RequirementsReaderAgent_Execute_SetsStatusToCompleted()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"gnex-reqs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.md"), "# Test Epic\n## REQ-001 Test\nSome text.\n### Tags\nPatient\n");

        try
        {
            var parser = new RequirementParser(new Mock<ILogger<RequirementParser>>().Object, new Mock<IServiceScopeFactory>().Object);
            var agent = new RequirementsReaderAgent(parser, new Mock<ILlmProvider>().Object, new Mock<ILogger<RequirementsReaderAgent>>().Object);
            var ctx = new AgentContext { RequirementsBasePath = tempDir };

            // Act
            var result = await agent.ExecuteAsync(ctx);

            // Assert
            result.Success.Should().BeTrue();
            result.Agent.Should().Be(AgentType.RequirementsReader);
            ctx.AgentStatuses[AgentType.RequirementsReader].Should().Be(AgentStatus.Completed);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RequirementsReaderAgent_Execute_ReturnsSuccessWithEmptyResults_OnBadPath()
    {
        // Arrange — parser returns empty list for nonexistent path (no exception)
        var parser = new RequirementParser(new Mock<ILogger<RequirementParser>>().Object, new Mock<IServiceScopeFactory>().Object);
        var agent = new RequirementsReaderAgent(parser, new Mock<ILlmProvider>().Object, new Mock<ILogger<RequirementsReaderAgent>>().Object);
        var ctx = new AgentContext { RequirementsBasePath = @"C:\nonexistent\path\12345" };

        // Act
        var result = await agent.ExecuteAsync(ctx);

        // Assert — agent succeeds but with 0 requirements
        result.Success.Should().BeTrue();
        ctx.Requirements.Should().BeEmpty();
        ctx.AgentStatuses[AgentType.RequirementsReader].Should().Be(AgentStatus.Completed);
    }
}
