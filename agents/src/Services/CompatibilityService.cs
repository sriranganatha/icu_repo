using Hms.Database;
using Hms.Database.Entities.Platform.Configuration;
using Hms.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hms.Services.Platform;

/// <summary>Validates tech stack selections against compatibility rules.</summary>
public interface ICompatibilityService
{
    Task<List<CompatibilityIssue>> ValidateTechStackAsync(string projectId, CancellationToken ct = default);
    Task<List<CompatibilityRule>> ListRulesAsync(CancellationToken ct = default);
    Task<CompatibilityRule> CreateRuleAsync(CompatibilityRule rule, CancellationToken ct = default);
    Task DeleteRuleAsync(string ruleId, CancellationToken ct = default);
}

public record CompatibilityIssue(string SourceTech, string TargetTech, string Compatibility, string? Reason);

public class CompatibilityService(
    HmsDbContext db,
    IPlatformRepository<CompatibilityRule> ruleRepo,
    ILogger<CompatibilityService> logger) : ICompatibilityService
{
    public async Task<List<CompatibilityIssue>> ValidateTechStackAsync(string projectId, CancellationToken ct = default)
    {
        var techStack = await db.Set<Hms.Database.Entities.Platform.Projects.ProjectTechStack>()
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(ct);

        var rules = await db.CompatibilityRules.Where(r => r.IsActive).ToListAsync(ct);
        var issues = new List<CompatibilityIssue>();

        foreach (var tech in techStack)
        {
            foreach (var other in techStack.Where(t => t.Id != tech.Id))
            {
                var rule = rules.FirstOrDefault(r =>
                    (r.SourceTechnologyId == tech.TechnologyId && r.TargetTechnologyId == other.TechnologyId) ||
                    (r.SourceTechnologyId == other.TechnologyId && r.TargetTechnologyId == tech.TechnologyId));

                if (rule?.Compatibility == "incompatible")
                {
                    issues.Add(new CompatibilityIssue(tech.TechnologyId, other.TechnologyId, "incompatible", rule.Reason));
                }
            }
        }

        return issues;
    }

    public async Task<List<CompatibilityRule>> ListRulesAsync(CancellationToken ct = default)
        => await ruleRepo.ListAsync(ct: ct);

    public async Task<CompatibilityRule> CreateRuleAsync(CompatibilityRule rule, CancellationToken ct = default)
    {
        await ruleRepo.CreateAsync(rule, ct);
        return rule;
    }

    public async Task DeleteRuleAsync(string ruleId, CancellationToken ct = default)
        => await ruleRepo.SoftDeleteAsync(ruleId, ct);
}
