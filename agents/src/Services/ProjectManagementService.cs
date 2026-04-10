using System.Text.RegularExpressions;
using Hms.Database;
using Hms.Database.Entities.Platform.Projects;
using Hms.Database.Repositories;
using Hms.Services.Dtos.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hms.Services.Platform;

public sealed partial class ProjectManagementService : IProjectManagementService
{
    private readonly IProjectRepository _projectRepo;
    private readonly HmsDbContext _db;
    private readonly ILogger<ProjectManagementService> _logger;

    public ProjectManagementService(IProjectRepository projectRepo, HmsDbContext db, ILogger<ProjectManagementService> logger)
    {
        _projectRepo = projectRepo;
        _db = db;
        _logger = logger;
    }

    // ── Projects ──────────────────────────────────────────
    public async Task<ProjectDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var e = await _projectRepo.GetByIdAsync(id, ct);
        return e is null ? null : MapProject(e);
    }

    public async Task<ProjectDetailDto?> GetDetailAsync(string id, CancellationToken ct = default)
    {
        var e = await _projectRepo.GetWithDetailsAsync(id, ct);
        if (e is null) return null;
        return new ProjectDetailDto
        {
            Id = e.Id, Name = e.Name, Slug = e.Slug, Description = e.Description,
            ProjectType = e.ProjectType, Status = e.Status, OrganizationId = e.OrganizationId,
            CreatedAt = e.CreatedAt,
            Settings = e.Settings is null ? null : new ProjectSettingsDto
            {
                GitRepoUrl = e.Settings.GitRepoUrl, DefaultBranch = e.Settings.DefaultBranch,
                ArtifactStoragePath = e.Settings.ArtifactStoragePath,
                NotificationConfigJson = e.Settings.NotificationConfigJson
            },
            TechStack = e.TechStack?.Select(t => new ProjectTechStackDto
            {
                Id = t.Id, Layer = t.Layer, TechnologyId = t.TechnologyId,
                TechnologyType = t.TechnologyType, Version = t.Version,
                ConfigOverridesJson = t.ConfigOverridesJson
            }).ToList() ?? [],
            Environments = e.Environments?.Select(env => new EnvironmentConfigDto
            {
                Id = env.Id, EnvName = env.EnvName, VariablesJson = env.VariablesJson,
                InfraConfigJson = env.InfraConfigJson
            }).ToList() ?? [],
            EpicCount = await _db.Epics.CountAsync(ep => ep.ProjectId == id, ct),
            SprintCount = await _db.Sprints.CountAsync(s => s.ProjectId == id, ct)
        };
    }

    public async Task<List<ProjectDto>> ListAsync(string? status = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var items = status is not null
            ? await _projectRepo.ListByStatusAsync(status, skip, take, ct)
            : await _projectRepo.ListAsync(skip, take, ct);
        return items.Select(MapProject).ToList();
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        var slug = GenerateSlug(request.Name);
        var project = new Project
        {
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            ProjectType = request.ProjectType,
            OrganizationId = request.OrganizationId
        };
        await _projectRepo.CreateAsync(project, ct);

        // Create default settings
        var settings = new ProjectSettings { ProjectId = project.Id };
        _db.ProjectSettings.Add(settings);

        // Create default environments
        _db.EnvironmentConfigs.Add(new EnvironmentConfig { ProjectId = project.Id, EnvName = "development" });
        _db.EnvironmentConfigs.Add(new EnvironmentConfig { ProjectId = project.Id, EnvName = "staging" });
        _db.EnvironmentConfigs.Add(new EnvironmentConfig { ProjectId = project.Id, EnvName = "production" });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created project {Name} ({Slug}) of type {Type}", project.Name, project.Slug, project.ProjectType);
        return MapProject(project);
    }

    public async Task<ProjectDto> UpdateAsync(UpdateProjectRequest request, CancellationToken ct = default)
    {
        var entity = await _projectRepo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Project {request.Id} not found");
        if (request.Name is not null) { entity.Name = request.Name; entity.Slug = GenerateSlug(request.Name); }
        if (request.Description is not null) entity.Description = request.Description;
        if (request.Status is not null) entity.Status = request.Status;
        await _projectRepo.UpdateAsync(entity, ct);
        return MapProject(entity);
    }

    public Task ArchiveAsync(string id, CancellationToken ct = default)
        => _projectRepo.SoftDeleteAsync(id, ct);

    // ── Tech Stack ────────────────────────────────────────
    public async Task<ProjectTechStackDto> AddTechStackAsync(AddTechStackRequest request, CancellationToken ct = default)
    {
        var entity = new ProjectTechStack
        {
            ProjectId = request.ProjectId, Layer = request.Layer,
            TechnologyId = request.TechnologyId, TechnologyType = request.TechnologyType,
            Version = request.Version, ConfigOverridesJson = request.ConfigOverridesJson
        };
        _db.ProjectTechStacks.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new ProjectTechStackDto
        {
            Id = entity.Id, Layer = entity.Layer, TechnologyId = entity.TechnologyId,
            TechnologyType = entity.TechnologyType, Version = entity.Version,
            ConfigOverridesJson = entity.ConfigOverridesJson
        };
    }

    public async Task RemoveTechStackAsync(string techStackId, CancellationToken ct = default)
    {
        var entity = await _db.ProjectTechStacks.FindAsync([techStackId], ct);
        if (entity is not null)
        {
            _db.ProjectTechStacks.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Backlog ───────────────────────────────────────────
    public async Task<EpicDto> CreateEpicAsync(CreateEpicRequest request, CancellationToken ct = default)
    {
        var maxOrder = await _db.Epics.Where(e => e.ProjectId == request.ProjectId).MaxAsync(e => (int?)e.Order, ct) ?? 0;
        var entity = new Epic
        {
            ProjectId = request.ProjectId, Title = request.Title,
            Description = request.Description, Priority = request.Priority,
            BrdSectionId = request.BrdSectionId, Order = maxOrder + 1
        };
        _db.Epics.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapEpic(entity, 0);
    }

    public async Task<List<EpicDto>> ListEpicsAsync(string projectId, CancellationToken ct = default)
    {
        var epics = await _db.Epics
            .Where(e => e.ProjectId == projectId && e.IsActive)
            .OrderBy(e => e.Order)
            .Select(e => new { Epic = e, StoryCount = e.Stories.Count })
            .ToListAsync(ct);
        return epics.Select(x => MapEpic(x.Epic, x.StoryCount)).ToList();
    }

    public async Task<StoryDto> CreateStoryAsync(CreateStoryRequest request, CancellationToken ct = default)
    {
        var maxOrder = await _db.Stories.Where(s => s.EpicId == request.EpicId).MaxAsync(s => (int?)s.Order, ct) ?? 0;
        var entity = new Story
        {
            EpicId = request.EpicId, Title = request.Title,
            AcceptanceCriteriaJson = request.AcceptanceCriteriaJson,
            StoryPoints = request.StoryPoints, Order = maxOrder + 1
        };
        _db.Stories.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapStory(entity, 0);
    }

    public async Task<List<StoryDto>> ListStoriesAsync(string epicId, CancellationToken ct = default)
    {
        var stories = await _db.Stories
            .Where(s => s.EpicId == epicId && s.IsActive)
            .OrderBy(s => s.Order)
            .Select(s => new { Story = s, TaskCount = s.Tasks.Count })
            .ToListAsync(ct);
        return stories.Select(x => MapStory(x.Story, x.TaskCount)).ToList();
    }

    public async Task<TaskItemDto> CreateTaskItemAsync(CreateTaskItemRequest request, CancellationToken ct = default)
    {
        var maxOrder = await _db.TaskItems.Where(t => t.StoryId == request.StoryId).MaxAsync(t => (int?)t.Order, ct) ?? 0;
        var entity = new TaskItem
        {
            StoryId = request.StoryId, TaskType = request.TaskType,
            AssignedAgentType = request.AssignedAgentType,
            EstimatedTokens = request.EstimatedTokens, Order = maxOrder + 1
        };
        _db.TaskItems.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapTask(entity);
    }

    public async Task<List<TaskItemDto>> ListTasksAsync(string storyId, CancellationToken ct = default)
    {
        var tasks = await _db.TaskItems
            .Where(t => t.StoryId == storyId && t.IsActive)
            .OrderBy(t => t.Order).ToListAsync(ct);
        return tasks.Select(MapTask).ToList();
    }

    // ── Sprints ───────────────────────────────────────────
    public async Task<SprintDto> CreateSprintAsync(CreateSprintRequest request, CancellationToken ct = default)
    {
        var maxOrder = await _db.Sprints.Where(s => s.ProjectId == request.ProjectId).MaxAsync(s => (int?)s.Order, ct) ?? 0;
        var entity = new Sprint
        {
            ProjectId = request.ProjectId, Name = request.Name,
            Goal = request.Goal, Order = maxOrder + 1
        };
        _db.Sprints.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapSprint(entity, 0);
    }

    public async Task<List<SprintDto>> ListSprintsAsync(string projectId, CancellationToken ct = default)
    {
        var sprints = await _db.Sprints
            .Where(s => s.ProjectId == projectId && s.IsActive)
            .OrderBy(s => s.Order)
            .Select(s => new { Sprint = s, StoryCount = s.Stories.Count })
            .ToListAsync(ct);
        return sprints.Select(x => MapSprint(x.Sprint, x.StoryCount)).ToList();
    }

    // ── Metrics ───────────────────────────────────────────
    public async Task<List<QualityReportDto>> ListQualityReportsAsync(string projectId, CancellationToken ct = default)
    {
        var reports = await _db.QualityReports
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.GeneratedAt)
            .Take(20).ToListAsync(ct);
        return reports.Select(r => new QualityReportDto
        {
            Id = r.Id, ProjectId = r.ProjectId, SprintId = r.SprintId,
            CoveragePercent = r.CoveragePercent, LintErrors = r.LintErrors,
            ComplexityScore = r.ComplexityScore, SecurityVulnerabilities = r.SecurityVulnerabilities,
            GeneratedAt = r.GeneratedAt
        }).ToList();
    }

    public async Task<List<ProjectMetricDto>> ListMetricsAsync(string projectId, string? metricType = null, CancellationToken ct = default)
    {
        var query = _db.ProjectMetrics.Where(m => m.ProjectId == projectId);
        if (metricType is not null) query = query.Where(m => m.MetricType == metricType);
        var metrics = await query.OrderByDescending(m => m.RecordedAt).Take(100).ToListAsync(ct);
        return metrics.Select(m => new ProjectMetricDto
        {
            Id = m.Id, ProjectId = m.ProjectId, MetricType = m.MetricType,
            Value = m.Value, RecordedAt = m.RecordedAt
        }).ToList();
    }

    // ── Helpers ───────────────────────────────────────────
    private static string GenerateSlug(string name) =>
        SlugRegex().Replace(name.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    private static ProjectDto MapProject(Project e) => new()
    {
        Id = e.Id, Name = e.Name, Slug = e.Slug, Description = e.Description,
        ProjectType = e.ProjectType, Status = e.Status,
        OrganizationId = e.OrganizationId, CreatedAt = e.CreatedAt
    };

    private static EpicDto MapEpic(Epic e, int storyCount) => new()
    {
        Id = e.Id, ProjectId = e.ProjectId, Title = e.Title,
        Description = e.Description, Priority = e.Priority, Status = e.Status,
        StoryCount = storyCount
    };

    private static StoryDto MapStory(Story e, int taskCount) => new()
    {
        Id = e.Id, EpicId = e.EpicId, Title = e.Title,
        AcceptanceCriteriaJson = e.AcceptanceCriteriaJson, StoryPoints = e.StoryPoints,
        SprintId = e.SprintId, Status = e.Status, TaskCount = taskCount
    };

    private static TaskItemDto MapTask(TaskItem e) => new()
    {
        Id = e.Id, StoryId = e.StoryId, TaskType = e.TaskType,
        AssignedAgentType = e.AssignedAgentType, Status = e.Status,
        EstimatedTokens = e.EstimatedTokens
    };

    private static SprintDto MapSprint(Sprint e, int storyCount) => new()
    {
        Id = e.Id, ProjectId = e.ProjectId, Name = e.Name,
        Goal = e.Goal, Order = e.Order, Status = e.Status,
        StartDate = e.StartDate, EndDate = e.EndDate, StoryCount = storyCount
    };
}
