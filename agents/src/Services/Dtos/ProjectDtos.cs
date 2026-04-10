namespace GNex.Services.Dtos.Platform;

// ── Project DTOs ──────────────────────────────────────────
public sealed record ProjectDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProjectType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? OrganizationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ProjectDetailDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProjectType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? OrganizationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public ProjectSettingsDto? Settings { get; init; }
    public List<ProjectTechStackDto> TechStack { get; init; } = [];
    public List<EnvironmentConfigDto> Environments { get; init; } = [];
    public int EpicCount { get; init; }
    public int SprintCount { get; init; }
}

public sealed record ProjectSettingsDto
{
    public string? GitRepoUrl { get; init; }
    public string DefaultBranch { get; init; } = "main";
    public string? ArtifactStoragePath { get; init; }
    public string NotificationConfigJson { get; init; } = "{}";
}

public sealed record ProjectTechStackDto
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public string TechnologyId { get; init; } = string.Empty;
    public string TechnologyType { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? ConfigOverridesJson { get; init; }
}

public sealed record EnvironmentConfigDto
{
    public string Id { get; init; } = string.Empty;
    public string EnvName { get; init; } = string.Empty;
    public string VariablesJson { get; init; } = "{}";
    public string? InfraConfigJson { get; init; }
}

public sealed record CreateProjectRequest
{
    public required string Name { get; init; }
    public required string ProjectType { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? OrganizationId { get; init; }
    public string? StarterKitId { get; init; }
}

public sealed record UpdateProjectRequest
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }
}

public sealed record AddTechStackRequest
{
    public required string ProjectId { get; init; }
    public required string Layer { get; init; }
    public required string TechnologyId { get; init; }
    public required string TechnologyType { get; init; }
    public required string Version { get; init; }
    public string? ConfigOverridesJson { get; init; }
}

// ── Backlog DTOs ──────────────────────────────────────────
public sealed record EpicDto
{
    public string Id { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Priority { get; init; } = "medium";
    public string Status { get; init; } = "draft";
    public int StoryCount { get; init; }
}

public sealed record StoryDto
{
    public string Id { get; init; } = string.Empty;
    public string EpicId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string AcceptanceCriteriaJson { get; init; } = "[]";
    public int? StoryPoints { get; init; }
    public string? SprintId { get; init; }
    public string Status { get; init; } = "backlog";
    public int TaskCount { get; init; }
}

public sealed record TaskItemDto
{
    public string Id { get; init; } = string.Empty;
    public string StoryId { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public string? AssignedAgentType { get; init; }
    public string Status { get; init; } = "pending";
    public int? EstimatedTokens { get; init; }
}

public sealed record SprintDto
{
    public string Id { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Goal { get; init; }
    public int Order { get; init; }
    public string Status { get; init; } = "planning";
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public int StoryCount { get; init; }
}

public sealed record CreateEpicRequest
{
    public required string ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string Priority { get; init; } = "medium";
    public string? BrdSectionId { get; init; }
}

public sealed record CreateStoryRequest
{
    public required string EpicId { get; init; }
    public required string Title { get; init; }
    public string AcceptanceCriteriaJson { get; init; } = "[]";
    public int? StoryPoints { get; init; }
}

public sealed record CreateTaskItemRequest
{
    public required string StoryId { get; init; }
    public required string TaskType { get; init; }
    public string? AssignedAgentType { get; init; }
    public int? EstimatedTokens { get; init; }
}

public sealed record CreateSprintRequest
{
    public required string ProjectId { get; init; }
    public required string Name { get; init; }
    public string? Goal { get; init; }
}

// ── Analytics DTOs ────────────────────────────────────────
public sealed record QualityReportDto
{
    public string Id { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string? SprintId { get; init; }
    public decimal? CoveragePercent { get; init; }
    public int? LintErrors { get; init; }
    public decimal? ComplexityScore { get; init; }
    public int? SecurityVulnerabilities { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ProjectMetricDto
{
    public string Id { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string MetricType { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}
