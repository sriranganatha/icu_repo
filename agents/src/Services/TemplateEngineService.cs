using Hms.Database;
using Hms.Database.Entities.Platform.Configuration;
using Hms.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Hms.Services.Platform;

/// <summary>Resolves template variables like {{project.name}}, {{project.tech_stack.backend.framework}}.</summary>
public interface ITemplateEngineService
{
    Task<string> RenderAsync(string template, string projectId, CancellationToken ct = default);
    Task<List<string>> ExtractVariablesAsync(string template, CancellationToken ct = default);
    Task<List<string>> ValidateTemplateAsync(string template, CancellationToken ct = default);
    Task<List<TemplateVariable>> ListVariablesAsync(CancellationToken ct = default);
}

public partial class TemplateEngineService(
    HmsDbContext db,
    IPlatformRepository<TemplateVariable> varRepo,
    ILogger<TemplateEngineService> logger) : ITemplateEngineService
{
    [GeneratedRegex(@"\{\{([a-zA-Z0-9_.]+)\}\}")]
    private static partial Regex VariablePattern();

    public async Task<string> RenderAsync(string template, string projectId, CancellationToken ct = default)
    {
        var context = await BuildContextAsync(projectId, ct);
        return VariablePattern().Replace(template, match =>
        {
            var varName = match.Groups[1].Value;
            return context.TryGetValue(varName, out var value) ? value : match.Value;
        });
    }

    public Task<List<string>> ExtractVariablesAsync(string template, CancellationToken ct = default)
    {
        var vars = VariablePattern().Matches(template)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        return Task.FromResult(vars);
    }

    public async Task<List<string>> ValidateTemplateAsync(string template, CancellationToken ct = default)
    {
        var usedVars = await ExtractVariablesAsync(template, ct);
        var knownVars = await db.TemplateVariables.Select(v => v.Name).ToListAsync(ct);
        var errors = usedVars.Where(v => !knownVars.Contains(v)).Select(v => $"Undefined variable: {{{{{v}}}}}").ToList();
        return errors;
    }

    public async Task<List<TemplateVariable>> ListVariablesAsync(CancellationToken ct = default)
        => await varRepo.ListAsync(ct: ct);

    private async Task<Dictionary<string, string>> BuildContextAsync(string projectId, CancellationToken ct)
    {
        var context = new Dictionary<string, string>();
        var project = await db.Projects
            .Include(p => p.TechStack)
            .Include(p => p.Settings)
            .Include(p => p.Environments)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null) return context;

        context["project.name"] = project.Name;
        context["project.slug"] = project.Slug;
        context["project.type"] = project.ProjectType;
        context["project.description"] = project.Description ?? string.Empty;
        context["project.status"] = project.Status;

        // Tech stack uses Layer + TechnologyType
        var backendLang = project.TechStack.FirstOrDefault(t => t.Layer == "backend" && t.TechnologyType == "language");
        context["project.tech_stack.language"] = backendLang?.TechnologyId ?? string.Empty;
        context["project.tech_stack.language.version"] = backendLang?.Version ?? string.Empty;

        var backendFw = project.TechStack.FirstOrDefault(t => t.Layer == "backend" && t.TechnologyType == "framework");
        context["project.tech_stack.backend.framework"] = backendFw?.TechnologyId ?? string.Empty;

        var dbTech = project.TechStack.FirstOrDefault(t => t.TechnologyType == "database");
        context["project.tech_stack.database"] = dbTech?.TechnologyId ?? string.Empty;

        var devEnv = project.Environments.FirstOrDefault(e => e.EnvName == "development");
        context["project.database.connection_string"] = devEnv?.VariablesJson ?? string.Empty;

        // Settings
        if (project.Settings is not null)
        {
            context["project.settings.git_repo_url"] = project.Settings.GitRepoUrl ?? string.Empty;
            context["project.settings.default_branch"] = project.Settings.DefaultBranch ?? string.Empty;
        }

        return context;
    }
}
