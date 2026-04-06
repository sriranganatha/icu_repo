using HmsAgents.Core.Models;

namespace HmsAgents.Agents.Requirements;

internal static class RequirementQualityScorer
{
    public static List<RequirementQualityScore> ScoreAll(List<Requirement> requirements)
    {
        return requirements.Select(ScoreOne).ToList();
    }

    private static RequirementQualityScore ScoreOne(Requirement req)
    {
        var notes = new List<string>();
        var text = $"{req.Title} {req.Description}".ToLowerInvariant();

        var independent = req.DependsOn.Count == 0;
        if (!independent)
            notes.Add($"Has {req.DependsOn.Count} dependency links; consider splitting or decoupling.");

        var negotiable = !ContainsAny(text,
            "must use", "exactly", "hardcode", "strictly implement", "fixed ui", "pixel-perfect only");
        if (!negotiable)
            notes.Add("Contains implementation-constraining language; keep room for technical negotiation.");

        var valuable = req.Title.Length >= 6 && req.Description.Length >= 20;
        if (!valuable)
            notes.Add("Business value is not clear enough in title/description.");

        var estimable = req.Description.Length >= 40 && req.AcceptanceCriteria.Count > 0;
        if (!estimable)
            notes.Add("Needs richer detail and at least one acceptance criterion for estimation.");

        var small = req.AcceptanceCriteria.Count <= 7 && req.Description.Length <= 600;
        if (!small)
            notes.Add("Likely too large; split into smaller stories/tasks.");

        var testable = req.AcceptanceCriteria.Count > 0 &&
                       req.AcceptanceCriteria.All(ac => ContainsAny(ac.ToLowerInvariant(), "given", "when", "then"));
        if (!testable)
            notes.Add("Acceptance criteria should use Given/When/Then and be pass/fail testable.");

        var score = 0;
        score += independent ? 16 : 0;
        score += negotiable ? 16 : 0;
        score += valuable ? 17 : 0;
        score += estimable ? 17 : 0;
        score += small ? 17 : 0;
        score += testable ? 17 : 0;

        var isReady = score >= 70 && estimable && testable && valuable;
        if (isReady)
            notes.Add("Ready: passes INVEST baseline for expansion.");

        return new RequirementQualityScore
        {
            RequirementId = req.Id,
            Title = req.Title,
            Score = score,
            Independent = independent,
            Negotiable = negotiable,
            Valuable = valuable,
            Estimable = estimable,
            Small = small,
            Testable = testable,
            IsReady = isReady,
            Notes = notes
        };
    }

    private static bool ContainsAny(string source, params string[] needles)
        => needles.Any(n => source.Contains(n, StringComparison.OrdinalIgnoreCase));
}
