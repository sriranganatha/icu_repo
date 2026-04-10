using System.Text;
using GNex.Core.Models;

namespace GNex.Agents.Brd;

/// <summary>
/// Exports a BrdDocument to a well-structured Markdown file.
/// </summary>
public static class BrdMarkdownExporter
{
    public static string Export(BrdDocument brd)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {brd.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Status:** {brd.Status}  ");
        sb.AppendLine($"**Generated:** {brd.CreatedAt:yyyy-MM-dd HH:mm} UTC  ");
        sb.AppendLine($"**Run ID:** `{brd.RunId}`  ");
        sb.AppendLine($"**Traced Requirements:** {brd.TracedRequirementIds.Count}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Table of Contents
        sb.AppendLine("## Table of Contents");
        sb.AppendLine();
        sb.AppendLine("1. [Executive Summary](#1-executive-summary)");
        sb.AppendLine("2. [Project Scope](#2-project-scope)");
        sb.AppendLine("3. [Stakeholders](#3-stakeholders)");
        sb.AppendLine("4. [Functional Requirements](#4-functional-requirements)");
        sb.AppendLine("5. [Non-Functional Requirements](#5-non-functional-requirements)");
        sb.AppendLine("6. [Assumptions & Constraints](#6-assumptions--constraints)");
        sb.AppendLine("7. [Integration Points](#7-integration-points)");
        sb.AppendLine("8. [Security Requirements](#8-security-requirements)");
        sb.AppendLine("9. [Performance Requirements](#9-performance-requirements)");
        sb.AppendLine("10. [Data Requirements](#10-data-requirements)");
        sb.AppendLine("11. [Risk Assessment](#11-risk-assessment)");
        sb.AppendLine("12. [Dependencies](#12-dependencies)");
        sb.AppendLine("13. [Diagrams](#13-diagrams)");
        sb.AppendLine();

        // 1. Executive Summary
        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine();
        sb.AppendLine(brd.ExecutiveSummary ?? "_Not yet specified._");
        sb.AppendLine();

        // 2. Scope
        sb.AppendLine("## 2. Project Scope");
        sb.AppendLine();
        sb.AppendLine(brd.ProjectScope ?? "_Not yet specified._");
        sb.AppendLine();
        sb.AppendLine("### In Scope");
        sb.AppendLine();
        sb.AppendLine(brd.InScope ?? "_All listed requirements._");
        sb.AppendLine();
        sb.AppendLine("### Out of Scope");
        sb.AppendLine();
        sb.AppendLine(brd.OutOfScope ?? "_None specified._");
        sb.AppendLine();

        // 3. Stakeholders
        AppendList(sb, "3. Stakeholders", brd.Stakeholders);

        // 4. Functional Requirements
        AppendList(sb, "4. Functional Requirements", brd.FunctionalRequirements);

        // 5. Non-Functional Requirements
        AppendList(sb, "5. Non-Functional Requirements", brd.NonFunctionalRequirements);

        // 6. Assumptions & Constraints
        sb.AppendLine("## 6. Assumptions & Constraints");
        sb.AppendLine();
        sb.AppendLine("### Assumptions");
        sb.AppendLine();
        foreach (var a in brd.Assumptions) sb.AppendLine($"- {a}");
        sb.AppendLine();
        sb.AppendLine("### Constraints");
        sb.AppendLine();
        foreach (var c in brd.Constraints) sb.AppendLine($"- {c}");
        sb.AppendLine();

        // 7. Integration Points
        AppendList(sb, "7. Integration Points", brd.IntegrationPoints);

        // 8. Security
        AppendList(sb, "8. Security Requirements", brd.SecurityRequirements);

        // 9. Performance
        AppendList(sb, "9. Performance Requirements", brd.PerformanceRequirements);

        // 10. Data
        AppendList(sb, "10. Data Requirements", brd.DataRequirements);

        // 11. Risks
        sb.AppendLine("## 11. Risk Assessment");
        sb.AppendLine();
        if (brd.Risks.Count > 0)
        {
            sb.AppendLine("| # | Risk | Impact | Likelihood | Mitigation |");
            sb.AppendLine("|---|------|--------|------------|------------|");
            for (var i = 0; i < brd.Risks.Count; i++)
            {
                var r = brd.Risks[i];
                sb.AppendLine($"| {i + 1} | {r.Description} | {r.Impact} | {r.Likelihood} | {r.Mitigation} |");
            }
        }
        else
        {
            sb.AppendLine("_No risks identified._");
        }
        sb.AppendLine();

        // 12. Dependencies
        AppendList(sb, "12. Dependencies", brd.Dependencies);

        // 13. Diagrams
        sb.AppendLine("## 13. Diagrams");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(brd.ContextDiagram))
        {
            sb.AppendLine("### System Context Diagram");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine(brd.ContextDiagram);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(brd.DataFlowDiagram))
        {
            sb.AppendLine("### Data Flow Diagram");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine(brd.DataFlowDiagram);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(brd.SequenceDiagram))
        {
            sb.AppendLine("### Sequence Diagram");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine(brd.SequenceDiagram);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(brd.ErDiagram))
        {
            sb.AppendLine("### Entity Relationship Diagram");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine(brd.ErDiagram);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Review Comments
        if (brd.ReviewComments.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Review Comments");
            sb.AppendLine();
            foreach (var c in brd.ReviewComments)
            {
                sb.AppendLine($"- **[{c.Action}]** ({c.Author}, {c.Timestamp:yyyy-MM-dd}): {c.Content}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string heading, List<string> items)
    {
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        if (items.Count > 0)
        {
            foreach (var item in items) sb.AppendLine($"- {item}");
        }
        else
        {
            sb.AppendLine("_None specified._");
        }
        sb.AppendLine();
    }
}
