using System.Collections.Concurrent;
using GNex.Core.Models;

namespace GNex.Agents.Requirements;

/// <summary>
/// Manages requirement version history with semantic diffing and rollback.
/// </summary>
public sealed class RequirementVersionTracker
{
    private readonly ConcurrentDictionary<string, List<RequirementVersion>> _history = new(StringComparer.OrdinalIgnoreCase);

    public RequirementVersion Snapshot(Requirement req, string changeReason = "")
    {
        var versions = _history.GetOrAdd(req.Id, _ => []);
        lock (versions)
        {
            var version = new RequirementVersion
            {
                RequirementId = req.Id,
                Version = versions.Count + 1,
                Title = req.Title,
                Description = req.Description,
                AcceptanceCriteria = [.. req.AcceptanceCriteria],
                Tags = [.. req.Tags],
                Module = req.Module,
                ChangeReason = changeReason
            };
            versions.Add(version);
            return version;
        }
    }

    public IReadOnlyList<RequirementVersion> GetHistory(string requirementId)
    {
        return _history.TryGetValue(requirementId, out var versions)
            ? versions.AsReadOnly()
            : [];
    }

    public RequirementDiff? Diff(string requirementId, int fromVersion, int toVersion)
    {
        if (!_history.TryGetValue(requirementId, out var versions))
            return null;
        RequirementVersion? from, to;
        lock (versions)
        {
            from = versions.Find(v => v.Version == fromVersion);
            to = versions.Find(v => v.Version == toVersion);
        }
        if (from is null || to is null) return null;
        return ComputeDiff(from, to);
    }

    public RequirementDiff? DiffLatest(string requirementId)
    {
        if (!_history.TryGetValue(requirementId, out var versions))
            return null;
        lock (versions)
        {
            if (versions.Count < 2) return null;
            return ComputeDiff(versions[^2], versions[^1]);
        }
    }

    public RequirementVersion? GetVersion(string requirementId, int version)
    {
        if (!_history.TryGetValue(requirementId, out var versions)) return null;
        lock (versions) { return versions.Find(v => v.Version == version); }
    }

    public void ExportTo(AgentContext context)
    {
        foreach (var kvp in _history)
        {
            List<RequirementVersion> snapshot;
            lock (kvp.Value) { snapshot = [.. kvp.Value]; }
            foreach (var v in snapshot) context.RequirementVersions.Add(v);
        }
    }

    private static RequirementDiff ComputeDiff(RequirementVersion from, RequirementVersion to)
    {
        var addedCriteria = to.AcceptanceCriteria.Except(from.AcceptanceCriteria, StringComparer.OrdinalIgnoreCase).ToList();
        var removedCriteria = from.AcceptanceCriteria.Except(to.AcceptanceCriteria, StringComparer.OrdinalIgnoreCase).ToList();
        var addedTags = to.Tags.Except(from.Tags, StringComparer.OrdinalIgnoreCase).ToList();
        var removedTags = from.Tags.Except(to.Tags, StringComparer.OrdinalIgnoreCase).ToList();

        var changes = new List<string>();
        if (!string.Equals(from.Title, to.Title, StringComparison.Ordinal)) changes.Add($"Title: \"{from.Title}\" → \"{to.Title}\"");
        if (!string.Equals(from.Description, to.Description, StringComparison.Ordinal)) changes.Add("Description changed");
        if (addedCriteria.Count > 0) changes.Add($"+{addedCriteria.Count} acceptance criteria");
        if (removedCriteria.Count > 0) changes.Add($"-{removedCriteria.Count} acceptance criteria");

        return new RequirementDiff
        {
            RequirementId = from.RequirementId,
            FromVersion = from.Version,
            ToVersion = to.Version,
            TitleChanged = !string.Equals(from.Title, to.Title, StringComparison.Ordinal),
            DescriptionChanged = !string.Equals(from.Description, to.Description, StringComparison.Ordinal),
            AddedCriteria = addedCriteria,
            RemovedCriteria = removedCriteria,
            AddedTags = addedTags,
            RemovedTags = removedTags,
            Summary = changes.Count > 0 ? string.Join("; ", changes) : "No changes"
        };
    }
}
