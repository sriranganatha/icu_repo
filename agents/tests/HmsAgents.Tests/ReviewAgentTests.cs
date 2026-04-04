using FluentAssertions;
using HmsAgents.Agents.Review;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace HmsAgents.Tests;

public class ReviewAgentTests
{
    private readonly ReviewAgent _agent = new(new Mock<ILogger<ReviewAgent>>().Object);

    [Fact]
    public async Task Execute_WithArtifacts_ProducesFindings()
    {
        var ctx = new AgentContext();
        ctx.Requirements.Add(new Requirement { Id = "REQ-001", Title = "Test", Tags = ["Patient"] });
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database,
            FileName = "PatientProfile.cs",
            RelativePath = "Patient/PatientProfile.cs",
            Content = "public class PatientProfile { public string TenantId {get;set;} public DateTimeOffset CreatedAt {get;set;} }",
            Namespace = "Hms.Patient",
            ProducedBy = AgentType.Database,
            TracedRequirementIds = ["REQ-001"]
        });

        var result = await _agent.ExecuteAsync(ctx);

        result.Agent.Should().Be(AgentType.Review);
        result.Findings.Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_MissingTenantId_FlagsSecurityViolation()
    {
        var ctx = new AgentContext();
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database,
            FileName = "BadEntity.cs",
            RelativePath = "Patient/BadEntity.cs",
            Content = "public class BadEntity { public Guid Id {get;set;} }", // No TenantId
            ProducedBy = AgentType.Database
        });

        var result = await _agent.ExecuteAsync(ctx);

        result.Findings.Should().Contain(f => f.Category == "MultiTenant" && f.Severity == ReviewSeverity.SecurityViolation);
    }

    [Fact]
    public async Task Execute_MissingAuditColumns_FlagsComplianceViolation()
    {
        var ctx = new AgentContext();
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Database,
            FileName = "NoAudit.cs",
            RelativePath = "Patient/NoAudit.cs",
            Content = "public class NoAudit { public string TenantId {get;set;} }", // No CreatedAt
            ProducedBy = AgentType.Database
        });

        var result = await _agent.ExecuteAsync(ctx);

        result.Findings.Should().Contain(f => f.Category == "Audit" && f.Severity == ReviewSeverity.ComplianceViolation);
    }

    [Fact]
    public async Task Execute_NoRLS_FlagsError()
    {
        var ctx = new AgentContext();
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Service,
            FileName = "SomeService.cs",
            RelativePath = "Patient/SomeService.cs",
            Content = "public class SomeService { }",
            ProducedBy = AgentType.ServiceLayer
        });

        var result = await _agent.ExecuteAsync(ctx);

        result.Findings.Should().Contain(f => f.Category == "MultiTenant" && f.Message.Contains("Row-Level Security"));
    }

    [Fact]
    public async Task Execute_NoTracedRequirements_FlagsWarning()
    {
        var ctx = new AgentContext();
        ctx.Artifacts.Add(new CodeArtifact
        {
            Layer = ArtifactLayer.Service,
            FileName = "Untraced.cs",
            RelativePath = "Patient/Untraced.cs",
            Content = "public class Untraced { }",
            Namespace = "Hms.Patient",
            ProducedBy = AgentType.ServiceLayer,
            TracedRequirementIds = [] // empty!
        });

        var result = await _agent.ExecuteAsync(ctx);

        result.Findings.Should().Contain(f => f.Category == "Traceability");
    }

    [Fact]
    public async Task Execute_EmptyContext_ReportsMultiTenancyErrors()
    {
        var ctx = new AgentContext();
        var result = await _agent.ExecuteAsync(ctx);

        // Empty context still triggers cross-cutting checks (no RLS, no QueryFilter)
        result.Findings.Should().Contain(f => f.Category == "MultiTenant");
        result.Success.Should().BeFalse("cross-cutting multi-tenancy checks produce blocking errors");
    }
}
