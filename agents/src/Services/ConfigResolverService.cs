using GNex.Database;
using GNex.Database.Entities.Platform.Configuration;
using GNex.Database.Entities.Platform.Projects;
using GNex.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GNex.Services.Platform;

/// <summary>3-tier configuration resolver: Master Default → Organization Override → Project Override.</summary>
public interface IConfigResolverService
{
    Task<JsonObject> ResolveConfigAsync(string projectId, CancellationToken ct = default);
    Task<ConfigSnapshot> CreateSnapshotAsync(string projectId, string snapshotType, string? triggerReason = null, CancellationToken ct = default);
    Task<ConfigSnapshot?> GetSnapshotAsync(string snapshotId, CancellationToken ct = default);
    Task<List<ConfigSnapshot>> ListSnapshotsAsync(string projectId, int limit = 20, CancellationToken ct = default);
}

public class ConfigResolverService(
    GNexDbContext db,
    IPlatformRepository<ConfigSnapshot> snapshotRepo,
    ILogger<ConfigResolverService> logger) : IConfigResolverService
{
    public async Task<JsonObject> ResolveConfigAsync(string projectId, CancellationToken ct = default)
    {
        var project = await db.Projects
            .Include(p => p.Settings)
            .Include(p => p.TechStack)
            .Include(p => p.Environments)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null) return new JsonObject();

        // Layer 1: Master defaults
        var config = new JsonObject
        {
            ["architecture"] = new JsonObject { ["pattern"] = "monolith" },
            ["quality"] = new JsonObject { ["testCoverageThreshold"] = 80, ["codeReviewRequired"] = true },
            ["deployment"] = new JsonObject { ["strategy"] = "blue_green", ["autoRollback"] = true },
            ["agents"] = new JsonObject { ["maxRetries"] = 3, ["timeoutSeconds"] = 300 }
        };

        // Layer 2: Organization overrides via NotificationConfigJson (stored as JSON overrides)
        if (project.Settings?.NotificationConfigJson is { } orgJson)
        {
            try { MergeJsonInto(config, orgJson); } catch { /* skip invalid JSON */ }
        }

        // Layer 3: Project-level overrides from tech stack and environment configs
        var techArray = new JsonArray();
        foreach (var ts in project.TechStack)
        {
            techArray.Add(new JsonObject { ["layer"] = ts.Layer, ["type"] = ts.TechnologyType, ["id"] = ts.TechnologyId, ["version"] = ts.Version });
        }
        config["techStack"] = techArray;

        if (project.Settings is not null)
        {
            config["project"] = new JsonObject
            {
                ["gitRepoUrl"] = project.Settings.GitRepoUrl ?? "",
                ["defaultBranch"] = project.Settings.DefaultBranch ?? "main",
                ["artifactStoragePath"] = project.Settings.ArtifactStoragePath ?? ""
            };
        }

        return config;
    }

    public async Task<ConfigSnapshot> CreateSnapshotAsync(string projectId, string snapshotType, string? triggerReason = null, CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct);
        var snapshot = new ConfigSnapshot
        {
            ProjectId = projectId,
            SnapshotType = snapshotType,
            ConfigJson = config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            TriggerReason = triggerReason
        };
        await snapshotRepo.CreateAsync(snapshot, ct);
        logger.LogInformation("Created config snapshot {SnapshotId} for project {ProjectId}", snapshot.Id, projectId);
        return snapshot;
    }

    public async Task<ConfigSnapshot?> GetSnapshotAsync(string snapshotId, CancellationToken ct = default)
        => await snapshotRepo.GetByIdAsync(snapshotId, ct);

    public async Task<List<ConfigSnapshot>> ListSnapshotsAsync(string projectId, int limit = 20, CancellationToken ct = default)
        => await db.ConfigSnapshots
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    private static void MergeJsonInto(JsonObject target, string json)
    {
        var overrides = JsonNode.Parse(json);
        if (overrides is not JsonObject obj) return;
        foreach (var (key, value) in obj)
        {
            if (value is JsonObject childObj && target[key] is JsonObject existingObj)
            {
                MergeJsonInto(existingObj, childObj.ToJsonString());
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
