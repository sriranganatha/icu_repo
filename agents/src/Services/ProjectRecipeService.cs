using Hms.Database;
using Hms.Database.Entities.Platform.Projects;
using Hms.Database.Entities.Platform.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hms.Services.Platform;

public record ProjectRecipe
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string ArchitecturePattern { get; init; } = "";
    public List<RecipeTechItem> TechStack { get; init; } = [];
    public RecipeWorkflow? Workflow { get; init; }
    public RecipeSettings? Settings { get; init; }
    public List<RecipeStandard> Standards { get; init; } = [];
    public string ExportedAt { get; init; } = "";
    public string ExportedFrom { get; init; } = "";
}

public record RecipeTechItem(string Layer, string TechnologyType, string TechnologyId, string? Version);
public record RecipeWorkflow(string Name, List<string> Stages);
public record RecipeSettings(string? DefaultBranch, string? GitRepoUrl);
public record RecipeStandard(string Type, string Name, string? Scope);

/// <summary>Export a project's config as a portable recipe, or import one to bootstrap a new project.</summary>
public interface IProjectRecipeService
{
    Task<ProjectRecipe> ExportAsync(string projectId, CancellationToken ct = default);
    Task<Project> ImportAsync(ProjectRecipe recipe, string tenantId, string createdBy, CancellationToken ct = default);
}

public class ProjectRecipeService(
    HmsDbContext db,
    ILogger<ProjectRecipeService> logger) : IProjectRecipeService
{
    public async Task<ProjectRecipe> ExportAsync(string projectId, CancellationToken ct = default)
    {
        var project = await db.Projects
            .Include(p => p.TechStack)
            .Include(p => p.Settings)
            .Include(p => p.Architectures)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found");

        var recipe = new ProjectRecipe
        {
            Name = project.Name,
            Description = project.Description,
            ArchitecturePattern = project.Architectures.FirstOrDefault()?.PatternId ?? "",
            TechStack = project.TechStack.Select(t => new RecipeTechItem(t.Layer, t.TechnologyType, t.TechnologyId, t.Version)).ToList(),
            Settings = project.Settings is { } s ? new RecipeSettings(s.DefaultBranch, s.GitRepoUrl) : null,
            ExportedAt = DateTimeOffset.UtcNow.ToString("O"),
            ExportedFrom = project.Slug
        };

        logger.LogInformation("Exported recipe for project {ProjectId}", projectId);
        return recipe;
    }

    public async Task<Project> ImportAsync(ProjectRecipe recipe, string tenantId, string createdBy, CancellationToken ct = default)
    {
        var project = new Project
        {
            Name = recipe.Name,
            Slug = recipe.Name.ToLowerInvariant().Replace(' ', '-'),
            Description = recipe.Description,
            Status = "Draft",
            TenantId = tenantId,
            CreatedBy = createdBy
        };

        foreach (var tech in recipe.TechStack)
        {
            project.TechStack.Add(new ProjectTechStack
            {
                ProjectId = project.Id,
                Layer = tech.Layer,
                TechnologyType = tech.TechnologyType,
                TechnologyId = tech.TechnologyId,
                Version = tech.Version ?? "",
                CreatedBy = createdBy
            });
        }

        if (recipe.Settings is { } settings)
        {
            project.Settings = new ProjectSettings
            {
                ProjectId = project.Id,
                DefaultBranch = settings.DefaultBranch ?? "main",
                GitRepoUrl = settings.GitRepoUrl ?? "",
                CreatedBy = createdBy
            };
        }

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Imported recipe as project {ProjectId} ({Name})", project.Id, project.Name);
        return project;
    }
}
