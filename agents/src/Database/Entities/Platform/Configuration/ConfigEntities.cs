namespace Hms.Database.Entities.Platform.Configuration;

/// <summary>Captures full resolved configuration at pipeline run start for reproducibility.</summary>
public class ConfigSnapshot : PlatformEntityBase
{
    public string ProjectId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string SnapshotType { get; set; } = "pipeline_run"; // pipeline_run, manual, scheduled
    public string ConfigJson { get; set; } = "{}"; // Full resolved config as JSON
    public string? TriggerReason { get; set; }
    public string? AgentRunId { get; set; }
    public string? PreviousSnapshotId { get; set; }
}

/// <summary>Tech stack compatibility rule between two technologies.</summary>
public class CompatibilityRule : PlatformEntityBase
{
    public string SourceTechnologyId { get; set; } = string.Empty;
    public string SourceTechnologyCategory { get; set; } = string.Empty; // language, framework, database, cloud
    public string TargetTechnologyId { get; set; } = string.Empty;
    public string TargetTechnologyCategory { get; set; } = string.Empty;
    public string Compatibility { get; set; } = "neutral"; // required, recommended, incompatible, neutral
    public string? Reason { get; set; }
    public string? VersionConstraint { get; set; }
}

/// <summary>Pre-configured project setup with tech stack, architecture, and template selections.</summary>
public class StarterKit : PlatformEntityBase
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-box";
    public string TechStackJson { get; set; } = "[]"; // Array of {category, technologyId, version}
    public string ArchitecturePattern { get; set; } = "monolith";
    public string? WorkflowId { get; set; }
    public string TemplatesJson { get; set; } = "{}"; // Map of template type → template ID
    public string? PreviewImageUrl { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Template variable definition for the template engine.</summary>
public class TemplateVariable : PlatformEntityBase
{
    public string Name { get; set; } = string.Empty; // e.g. project.name, project.tech_stack.backend.framework
    public string Scope { get; set; } = "project"; // project, organization, global
    public string Description { get; set; } = string.Empty;
    public string? ExampleValue { get; set; }
    public string ResolverType { get; set; } = "property"; // property, computed, custom
    public string? ResolverExpression { get; set; }
    public string DataType { get; set; } = "string"; // string, number, boolean, array, object
}

/// <summary>Agent plugin manifest for dynamically-defined agents.</summary>
public class AgentPluginManifest : PlatformEntityBase
{
    public string AgentTypeCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string ToolsJson { get; set; } = "[]"; // Array of tool names available to this agent
    public string ConstraintsJson { get; set; } = "{}"; // Constraints: max tokens, timeout, retries
    public string InputSchemaJson { get; set; } = "{}"; // Expected input shape
    public string OutputSchemaJson { get; set; } = "{}"; // Expected output shape
    public string? FallbackAgentTypeCode { get; set; }
    public bool IsEnabled { get; set; } = true;
}
