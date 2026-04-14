using FluentAssertions;
using GNex.Core.Enums;
using GNex.Core.Models;

namespace GNex.Tests.EndToEnd;

/// <summary>
/// Tests for PipelineConfig — defaults, computed properties, service ports,
/// domain context, and resume property wiring.
/// </summary>
public class PipelineConfigTests
{
    // ── Defaults ──

    [Fact]
    public void PipelineConfig_RequiredDefaults()
    {
        var config = new PipelineConfig();

        config.RequirementsPath.Should().BeEmpty();
        config.OutputPath.Should().BeEmpty();
        config.SolutionNamespace.Should().Be("GNex");
        config.DbHost.Should().Be("localhost");
        config.DbPort.Should().Be(5418);
        config.DbName.Should().Be("gnex_db");
        config.DbUser.Should().Be("gnex_admin");
        config.SpinUpDocker.Should().BeTrue();
        config.ExecuteDdl.Should().BeTrue();
        config.MaxQueueItems.Should().Be(10);
        config.MaxInDevItems.Should().Be(10);
        config.EnableIntegrationLayer.Should().BeTrue();
        config.EnableTestGeneration.Should().BeTrue();
        config.EnableReviewAgent.Should().BeTrue();
        config.EnableAgentCommunicationLogging.Should().BeTrue();
    }

    [Fact]
    public void PipelineConfig_ResumeProperties_DefaultNull()
    {
        var config = new PipelineConfig();

        config.ResumeCompletedAgents.Should().BeNull();
        config.ResumeRequirements.Should().BeNull();
        config.ResumeExpandedRequirements.Should().BeNull();
        config.ResumeDerivedServices.Should().BeNull();
    }

    [Fact]
    public void PipelineConfig_ServicePorts_HasGatewayAndKafka()
    {
        var config = new PipelineConfig();

        config.ServicePorts.Should().ContainKey("Gateway");
        config.ServicePorts.Should().ContainKey("Kafka");
        config.ServicePorts["Gateway"].Should().Be(5100);
        config.ServicePorts["Kafka"].Should().Be(9092);
    }

    [Fact]
    public void PipelineConfig_ServicePorts_CaseInsensitive()
    {
        var config = new PipelineConfig();

        config.ServicePorts["gateway"].Should().Be(5100);
        config.ServicePorts["KAFKA"].Should().Be(9092);
    }

    // ── Computed properties ──

    [Fact]
    public void PipelineConfig_ProjectPrefix_EmptyDomain_ReturnsApp()
    {
        var config = new PipelineConfig();
        config.ProjectPrefix.Should().Be("app");
    }

    [Fact]
    public void PipelineConfig_ProjectPrefix_Healthcare()
    {
        var config = new PipelineConfig { ProjectDomain = "Healthcare" };
        config.ProjectPrefix.Should().Be("healthcare");
    }

    [Fact]
    public void PipelineConfig_ProjectPrefix_SpacesAndSlashes_Stripped()
    {
        var config = new PipelineConfig { ProjectDomain = "Fin Tech / Banking" };
        config.ProjectPrefix.Should().Be("fintechbanking");
    }

    [Fact]
    public void PipelineConfig_ProjectLabel_EmptyDomain()
    {
        var config = new PipelineConfig();
        config.ProjectLabel.Should().Be("Application Platform");
    }

    [Fact]
    public void PipelineConfig_ProjectLabel_WithDomain()
    {
        var config = new PipelineConfig { ProjectDomain = "Healthcare" };
        config.ProjectLabel.Should().Be("Healthcare Platform");
    }

    [Fact]
    public void PipelineConfig_DomainContext_NoDomain()
    {
        var config = new PipelineConfig();
        config.DomainContext.Should().Be("a generic software platform");
    }

    [Fact]
    public void PipelineConfig_DomainContext_DomainOnly()
    {
        var config = new PipelineConfig { ProjectDomain = "Healthcare" };
        config.DomainContext.Should().Be("a Healthcare software platform");
    }

    [Fact]
    public void PipelineConfig_DomainContext_DescriptionOverrides()
    {
        var config = new PipelineConfig
        {
            ProjectDomain = "Healthcare",
            ProjectDomainDescription = "Hospital management system with HL7/FHIR interoperability"
        };
        config.DomainContext.Should().Be("Hospital management system with HL7/FHIR interoperability");
    }

    // ── Resume property combinations ──

    [Fact]
    public void PipelineConfig_Resume_FullContext()
    {
        var config = new PipelineConfig
        {
            ResumeCompletedAgents = new HashSet<string>
                { "RequirementsReader", "Architect", "RequirementsExpander" },
            ResumeRequirements =
            [
                new Requirement { Id = "REQ-001", Title = "Patient CRUD" },
                new Requirement { Id = "REQ-002", Title = "Encounter tracking" }
            ],
            ResumeExpandedRequirements =
            [
                new ExpandedRequirement { Id = "WI-001", Title = "Create patient table" },
                new ExpandedRequirement { Id = "WI-002", Title = "Add encounter API" }
            ],
            ResumeDerivedServices =
            [
                new MicroserviceDefinition
                {
                    Name = "PatientService", ShortName = "Patient", Schema = "patient",
                    Description = "Patient management", ApiPort = 5100,
                    Entities = ["Patient"], DependsOn = []
                }
            ]
        };

        config.ResumeCompletedAgents.Should().HaveCount(3);
        config.ResumeRequirements.Should().HaveCount(2);
        config.ResumeExpandedRequirements.Should().HaveCount(2);
        config.ResumeDerivedServices.Should().HaveCount(1);
    }

    [Fact]
    public void PipelineConfig_Resume_PartialContext_RequirementsOnly()
    {
        var config = new PipelineConfig
        {
            ResumeCompletedAgents = new HashSet<string> { "RequirementsReader" },
            ResumeRequirements =
            [
                new Requirement { Id = "REQ-001", Title = "Test" }
            ]
            // No expanded requirements, no derived services
        };

        config.ResumeExpandedRequirements.Should().BeNull();
        config.ResumeDerivedServices.Should().BeNull();
    }
}
