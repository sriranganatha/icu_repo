namespace Hms.Services.Dtos.Platform;

// ─── Template DTOs ──────────────────────────────────────────
public sealed record BrdTemplateDto(
    string Id, string Name, string ProjectType, string SectionsJson,
    bool IsDefault, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ArchitectureTemplateDto(
    string Id, string Name, string Pattern, string? DiagramTemplate,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CodeTemplateDto(
    string Id, string Name, string? LanguageId, string? FrameworkId,
    string TemplateType, string Content, string VariablesJson,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record FileStructureTemplateDto(
    string Id, string Name, string? FrameworkId, string TreeJson,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CiCdTemplateDto(
    string Id, string Name, string Provider, string? LanguageId,
    string PipelineYaml, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DockerTemplateDto(
    string Id, string Name, string? LanguageId, string? FrameworkId,
    string DockerfileContent, string? ComposeContent,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TestTemplateDto(
    string Id, string Name, string TestType, string? FrameworkId,
    string TestFramework, string TemplateContent,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record IaCTemplateDto(
    string Id, string Name, string? CloudProviderId, string Tool,
    string TemplateContent, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DocumentationTemplateDto(
    string Id, string Name, string DocType, string TemplateContent,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// ─── Create Requests ────────────────────────────────────────
public sealed record CreateBrdTemplateRequest(
    string Name, string ProjectType, string SectionsJson, bool IsDefault);

public sealed record CreateArchitectureTemplateRequest(
    string Name, string Pattern, string? DiagramTemplate);

public sealed record CreateCodeTemplateRequest(
    string Name, string? LanguageId, string? FrameworkId,
    string TemplateType, string Content, string VariablesJson);

public sealed record CreateFileStructureTemplateRequest(
    string Name, string? FrameworkId, string TreeJson);

public sealed record CreateCiCdTemplateRequest(
    string Name, string Provider, string? LanguageId, string PipelineYaml);

public sealed record CreateDockerTemplateRequest(
    string Name, string? LanguageId, string? FrameworkId,
    string DockerfileContent, string? ComposeContent);

public sealed record CreateTestTemplateRequest(
    string Name, string TestType, string? FrameworkId,
    string TestFramework, string TemplateContent);

public sealed record CreateIaCTemplateRequest(
    string Name, string? CloudProviderId, string Tool, string TemplateContent);

public sealed record CreateDocumentationTemplateRequest(
    string Name, string DocType, string TemplateContent);
