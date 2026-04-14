namespace GNex.Services.Dtos.Platform;

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

// ─── Update Requests ────────────────────────────────────────
public sealed record UpdateBrdTemplateRequest(
    string? Name, string? ProjectType, string? SectionsJson, bool? IsDefault);

public sealed record UpdateArchitectureTemplateRequest(
    string? Name, string? Pattern, string? DiagramTemplate);

public sealed record UpdateCodeTemplateRequest(
    string? Name, string? LanguageId, string? FrameworkId,
    string? TemplateType, string? Content, string? VariablesJson);

public sealed record UpdateFileStructureTemplateRequest(
    string? Name, string? FrameworkId, string? TreeJson);

public sealed record UpdateCiCdTemplateRequest(
    string? Name, string? Provider, string? LanguageId, string? PipelineYaml);

public sealed record UpdateDockerTemplateRequest(
    string? Name, string? LanguageId, string? FrameworkId,
    string? DockerfileContent, string? ComposeContent);

public sealed record UpdateTestTemplateRequest(
    string? Name, string? TestType, string? FrameworkId,
    string? TestFramework, string? TemplateContent);

public sealed record UpdateIaCTemplateRequest(
    string? Name, string? CloudProviderId, string? Tool, string? TemplateContent);

public sealed record UpdateDocumentationTemplateRequest(
    string? Name, string? DocType, string? TemplateContent);

// ─── BRD Upload DTOs ────────────────────────────────────────
public sealed record BrdUploadResult(
    string ProjectId, string RawRequirementId, int SectionsCreated, string Status);

public sealed record BrdBatchUploadResult(
    string ProjectId, int FilesProcessed, int TotalSectionsCreated, string Status,
    List<BrdFileResult> FileResults);

public sealed record BrdFileResult(
    string FileName, string RawRequirementId, int SectionsCreated, string Status);

public sealed record BrdSectionDto(
    string Id, string SectionType, int Order, string Content, string DiagramsJson);

public sealed record BrdProjectDto(
    string ProjectId, string ProjectName, string ProjectType, int DocumentCount,
    DateTimeOffset LastUpdated, string AggregateStatus);

public sealed record BrdEnrichResult(
    string BrdId, int SectionsEnriched, int SectionsFailed, string Status);

// ─── Multi-BRD DTOs ─────────────────────────────────────────
public sealed record BrdDocumentDto(
    string Id, string ProjectId, string ProjectName, string Title, string Description,
    string BrdType, string BrdTypeDisplay, string Instructions, string Status,
    int SectionCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt, string? ApprovedBy, string? GroupId);

public sealed record CreateBrdDocumentRequest(
    string ProjectId, string Title, string? Description, List<string> BrdTypes, string? Instructions);

public sealed record UpdateBrdDocumentRequest(
    string? Title, string? Description, string? Instructions, string? ProjectId = null);

public sealed record UpdateSectionRequest(string Content);

public sealed record BrdDocumentCreateResult(
    string ProjectId, int BrdsCreated, string? GroupId, List<BrdDocumentDto> Documents);

public sealed record WorkflowActionRequest(string Reviewer, string? Comment, string? Reason, string? Feedback);
