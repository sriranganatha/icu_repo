using HmsAgents.Agents.Brd;
using HmsAgents.Core.Models;

namespace HmsAgents.Tests;

public class BrdGeneratorTests
{
    [Fact]
    public void BrdDiagram_ContextDiagram_IncludesActorsFromRequirements()
    {
        var reqs = new List<Requirement>
        {
            new() { Id = "R1", Title = "Patient registration", Description = "Allow patient to register via portal" },
            new() { Id = "R2", Title = "Nurse triage", Description = "Nurse performs triage assessment" }
        };

        var diagram = BrdDiagramGenerator.GenerateContextDiagram(reqs, null);

        Assert.Contains("HMS", diagram);
        Assert.Contains("Patient", diagram);
        Assert.Contains("Nurse", diagram);
    }

    [Fact]
    public void BrdDiagram_ContextDiagram_IncludesExternalSystems()
    {
        var reqs = new List<Requirement>
        {
            new() { Id = "R1", Title = "FHIR integration", Description = "Send FHIR R4 resources to external server" }
        };

        var diagram = BrdDiagramGenerator.GenerateContextDiagram(reqs, null);

        Assert.Contains("FHIR Server", diagram);
    }

    [Fact]
    public void BrdDiagram_DataFlow_GeneratesModuleNodes()
    {
        var reqs = new List<Requirement>
        {
            new() { Id = "R1", Title = "Billing", Description = "Process claims", Module = "Billing" },
            new() { Id = "R2", Title = "Auth", Description = "User login", Module = "Auth" }
        };

        var diagram = BrdDiagramGenerator.GenerateDataFlowDiagram(reqs, null);

        Assert.Contains("Billing", diagram);
        Assert.Contains("Auth", diagram);
        Assert.Contains("API", diagram);
    }

    [Fact]
    public void BrdDiagram_Sequence_AlwaysIncludesStandardFlow()
    {
        var reqs = new List<Requirement> { new() { Id = "R1", Title = "Test" } };

        var diagram = BrdDiagramGenerator.GenerateSequenceDiagram(reqs);

        Assert.Contains("sequenceDiagram", diagram);
        Assert.Contains("HTTP Request", diagram);
        Assert.Contains("Audit", diagram);
    }

    [Fact]
    public void BrdDiagram_Er_FallbackWhenNoModel()
    {
        var diagram = BrdDiagramGenerator.GenerateErDiagram(null);

        Assert.Contains("erDiagram", diagram);
        Assert.Contains("PATIENT", diagram);
        Assert.Contains("ENCOUNTER", diagram);
    }

    [Fact]
    public void BrdDiagram_Er_UsesModelEntities()
    {
        var model = new ParsedDomainModel
        {
            Entities =
            [
                new ParsedEntity
                {
                    Name = "Invoice",
                    Fields = [
                        new EntityField { Name = "Id", Type = "Guid", IsKey = true },
                        new EntityField { Name = "PatientId", Type = "Guid" },
                        new EntityField { Name = "Amount", Type = "decimal" }
                    ]
                }
            ]
        };

        var diagram = BrdDiagramGenerator.GenerateErDiagram(model);

        Assert.Contains("INVOICE", diagram);
        Assert.Contains("Id PK", diagram);
        Assert.Contains("PatientId FK", diagram);
    }

    [Fact]
    public void BrdMarkdown_ExportsAllSections()
    {
        var brd = new BrdDocument
        {
            Title = "Test BRD",
            ExecutiveSummary = "Summary here",
            ProjectScope = "Scope here",
            Stakeholders = ["Admin"],
            FunctionalRequirements = ["FR1"],
            NonFunctionalRequirements = ["NFR1"],
            Assumptions = ["A1"],
            Constraints = ["C1"],
            IntegrationPoints = ["IP1"],
            SecurityRequirements = ["SR1"],
            PerformanceRequirements = ["PR1"],
            DataRequirements = ["DR1"],
            Risks = [new BrdRisk { Description = "Risk1", Impact = "High", Likelihood = "Medium", Mitigation = "M1" }],
            Dependencies = ["D1"],
            ContextDiagram = "graph TB\n  A-->B",
            TracedRequirementIds = ["R1"]
        };

        var md = BrdMarkdownExporter.Export(brd);

        Assert.Contains("# Test BRD", md);
        Assert.Contains("Executive Summary", md);
        Assert.Contains("Summary here", md);
        Assert.Contains("Stakeholders", md);
        Assert.Contains("Risk1", md);
        Assert.Contains("```mermaid", md);
        Assert.Contains("Table of Contents", md);
    }

    [Fact]
    public void BrdMarkdown_IncludesReviewComments()
    {
        var brd = new BrdDocument
        {
            Title = "BRD",
            ReviewComments =
            [
                new BrdComment { Author = "john", Content = "Looks good", Action = BrdCommentAction.Approve }
            ]
        };

        var md = BrdMarkdownExporter.Export(brd);

        Assert.Contains("Review Comments", md);
        Assert.Contains("john", md);
        Assert.Contains("Looks good", md);
    }
}
