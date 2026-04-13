using System.Text;
using GNex.Core.Models;

namespace GNex.Agents.Brd;

/// <summary>
/// Generates Mermaid diagrams for BRD documents.
/// </summary>
public static class BrdDiagramGenerator
{
    public static string GenerateContextDiagram(List<Requirement> requirements, ParsedDomainModel? domainModel, DomainProfile? domainProfile = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TB");
        var systemLabel = requirements.Select(r => r.Module).FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? "System";
        sb.AppendLine($"    SYS[{systemLabel}]");

        var actors = domainProfile?.ActorNames is { Count: > 0 } profileActors
            ? new HashSet<string>(profileActors, StringComparer.OrdinalIgnoreCase)
            : ExtractActors(requirements);
        var systems = domainProfile?.IntegrationPatterns is { Count: > 0 } patterns
            ? new HashSet<string>(patterns.Select(p => p.Name), StringComparer.OrdinalIgnoreCase)
            : ExtractExternalSystems(requirements);

        var idx = 0;
        foreach (var actor in actors)
        {
            var id = $"A{idx++}";
            sb.AppendLine($"    {id}((\"{actor}\"))");
            sb.AppendLine($"    {id} --> SYS");
        }

        idx = 0;
        foreach (var sys in systems)
        {
            var id = $"S{idx++}";
            sb.AppendLine($"    {id}[[\"{sys}\"]]");
            sb.AppendLine($"    SYS --> {id}");
        }

        if (domainModel?.Entities is { Count: > 0 })
        {
            var top = domainModel.Entities.Take(5).ToList();
            foreach (var e in top)
            {
                var id = e.Name.Replace(" ", "");
                sb.AppendLine($"    SYS --- {id}[\"{e.Name}\"]");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string GenerateDataFlowDiagram(List<Requirement> requirements, ParsedDomainModel? domainModel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        var modules = requirements
            .Select(r => r.Module)
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct()
            .ToList();

        if (modules.Count == 0)
            modules = ["Core"];

        sb.AppendLine("    User([\"User\"]) --> API[\"API Gateway\"]");
        foreach (var mod in modules)
        {
            var id = mod.Replace(" ", "").Replace("-", "");
            sb.AppendLine($"    API --> {id}[\"{mod} Service\"]");
            sb.AppendLine($"    {id} --> DB[(\"{mod} DB\")]");
        }

        var text = string.Join(" ", requirements.Select(r => r.Description ?? "")).ToLowerInvariant();
        if (text.Contains("kafka") || text.Contains("event") || text.Contains("async"))
        {
            sb.AppendLine("    API --> MQ{{\"Message Queue\"}}");
            foreach (var mod in modules.Take(3))
            {
                var id = mod.Replace(" ", "").Replace("-", "");
                sb.AppendLine($"    MQ --> {id}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string GenerateSequenceDiagram(List<Requirement> requirements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");
        sb.AppendLine("    participant U as User");
        sb.AppendLine("    participant API as API Gateway");
        sb.AppendLine("    participant SVC as Service Layer");
        sb.AppendLine("    participant DB as Database");
        sb.AppendLine("    participant AUD as Audit Log");

        sb.AppendLine("    U->>API: HTTP Request");
        sb.AppendLine("    API->>API: Authenticate & Authorize");
        sb.AppendLine("    API->>SVC: Process Request");
        sb.AppendLine("    SVC->>DB: Query / Mutate Data");
        sb.AppendLine("    DB-->>SVC: Result");
        sb.AppendLine("    SVC->>AUD: Log Audit Event");
        sb.AppendLine("    SVC-->>API: Response DTO");
        sb.AppendLine("    API-->>U: HTTP Response");

        var text = string.Join(" ", requirements.Select(r => r.Description ?? "")).ToLowerInvariant();
        if (text.Contains("notification") || text.Contains("alert"))
        {
            sb.AppendLine("    SVC->>SVC: Check Alert Rules");
            sb.AppendLine("    SVC-->>U: Push Notification");
        }

        return sb.ToString().TrimEnd();
    }

    public static string GenerateErDiagram(ParsedDomainModel? domainModel, DomainProfile? domainProfile = null)
    {
        if (domainModel?.Entities is not { Count: > 0 })
        {
            // Use LLM-generated fallback from DomainProfile, or a minimal generic diagram
            if (!string.IsNullOrWhiteSpace(domainProfile?.FallbackErDiagram))
                return domainProfile.FallbackErDiagram;

            return @"erDiagram
    ENTITY {
        uuid Id PK
        string Name
        string Status
    }
    AUDIT_LOG {
        uuid Id PK
        uuid EntityId FK
        datetime Timestamp
        string Action
    }
    ENTITY ||--o{ AUDIT_LOG : ""tracked by""";
        }

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        foreach (var entity in domainModel.Entities.Take(10))
        {
            sb.AppendLine($"    {entity.Name.ToUpperInvariant().Replace(" ", "_")} {{");
            foreach (var field in entity.Fields.Take(6))
            {
                var type = field.Type.Replace("?", "").Replace("<", "").Replace(">", "");
                var pk = field.IsKey ? " PK" : "";
                var fk = field.Name.EndsWith("Id", StringComparison.Ordinal) && !field.IsKey ? " FK" : "";
                sb.AppendLine($"        {type} {field.Name}{pk}{fk}");
            }
            sb.AppendLine("    }");
        }

        // Infer relationships from FK naming
        foreach (var entity in domainModel.Entities.Take(10))
        {
            foreach (var field in entity.Fields)
            {
                if (field.Name.EndsWith("Id", StringComparison.Ordinal) && !field.IsKey)
                {
                    var related = field.Name[..^2];
                    if (domainModel.Entities.Any(e => e.Name.Equals(related, StringComparison.OrdinalIgnoreCase)))
                    {
                        sb.AppendLine($"    {related.ToUpperInvariant()} ||--o{{ {entity.Name.ToUpperInvariant().Replace(" ", "_")} : \"\"");
                    }
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static HashSet<string> ExtractActors(List<Requirement> requirements)
    {
        // Generic keyword-based fallback when DomainProfile is not available
        var actors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}")).ToLowerInvariant();
        if (text.Contains("user") || text.Contains("customer") || text.Contains("client")) actors.Add("End User");
        if (text.Contains("admin")) actors.Add("Administrator");
        if (text.Contains("manager") || text.Contains("supervisor")) actors.Add("Manager");
        if (text.Contains("operator") || text.Contains("staff")) actors.Add("Operator");
        if (text.Contains("analyst") || text.Contains("report")) actors.Add("Analyst");
        if (actors.Count == 0) actors.Add("User");
        return actors;
    }

    private static HashSet<string> ExtractExternalSystems(List<Requirement> requirements)
    {
        // Generic keyword-based fallback when DomainProfile is not available
        var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = string.Join(" ", requirements.Select(r => $"{r.Title} {r.Description}")).ToLowerInvariant();
        if (text.Contains("kafka") || text.Contains("message queue")) systems.Add("Kafka Cluster");
        if (text.Contains("email") || text.Contains("smtp")) systems.Add("Email Service");
        if (text.Contains("ldap") || text.Contains("active directory")) systems.Add("Identity Provider");
        if (text.Contains("api") || text.Contains("integration")) systems.Add("External API");
        return systems;
    }
}
