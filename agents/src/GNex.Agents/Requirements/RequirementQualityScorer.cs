using GNex.Core.Models;

namespace GNex.Agents.Requirements;

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
        var clarifyingQuestions = BuildClarifyingQuestions(req, text);

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

        var criteriaCount = req.AcceptanceCriteria.Count;
        var criteriaWithGivenWhenThen = req.AcceptanceCriteria.Count(HasGivenWhenThen);
        var criteriaWithOutcome = req.AcceptanceCriteria.Count(HasOutcomeLanguage);
        var testable = criteriaCount > 0 &&
                       (criteriaWithGivenWhenThen >= Math.Max(1, criteriaCount / 2) ||
                        criteriaWithOutcome == criteriaCount);
        if (!testable)
            notes.Add("Acceptance criteria should be pass/fail testable; prefer Given/When/Then phrasing.");

        var score = 0;
        score += independent ? 16 : 0;
        score += negotiable ? 16 : 0;
        score += valuable ? 17 : 0;
        score += estimable ? 17 : 0;
        score += small ? 17 : 0;
        score += testable ? 17 : 0;

        if (clarifyingQuestions.Count > 0)
        {
            // Ambiguity adds risk to downstream decomposition; apply a bounded readiness penalty.
            score = Math.Max(0, score - Math.Min(20, clarifyingQuestions.Count * 5));
            notes.Add($"Ambiguity detected: {clarifyingQuestions.Count} clarification question(s) should be answered before coding.");
        }

        var isReady = score >= 70 && estimable && testable && valuable && clarifyingQuestions.Count == 0;
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
            Notes = notes,
            ClarifyingQuestions = clarifyingQuestions
        };
    }

    private static List<string> BuildClarifyingQuestions(Requirement req, string normalizedText)
    {
        var questions = new List<string>();

        if (ContainsAny(normalizedText,
                "fast", "quick", "user-friendly", "intuitive", "as needed", "etc", "and so on", "optimized", "best possible"))
        {
            questions.Add("Which measurable targets define this requirement (e.g., latency, throughput, accuracy, or SLA values)?");
        }

        if (!ContainsAny(normalizedText, "user", "admin", "operator", "manager", "system", "api", "service", "staff", "customer", "client", "agent"))
        {
            questions.Add("Who is the primary actor for this flow, and which secondary actors/systems are impacted?");
        }

        if (req.AcceptanceCriteria.Count == 0)
        {
            questions.Add("What are the explicit pass/fail acceptance criteria for this requirement?");
        }
        else if (req.AcceptanceCriteria.Any(c => c.Length < 20))
        {
            questions.Add("Can acceptance criteria be expanded into concrete Given/When/Then scenarios with expected outcomes?");
        }

        if (!ContainsAny(normalizedText, "must", "shall", "within", "under", "at least", "at most", "error", "validation"))
        {
            questions.Add("What constraints and validation rules must the system enforce for this requirement?");
        }

        return questions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ContainsAny(string source, params string[] needles)
        => needles.Any(n => source.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static bool HasGivenWhenThen(string criterion)
        => ContainsAny(criterion, "given") && ContainsAny(criterion, "when") && ContainsAny(criterion, "then");

    private static bool HasOutcomeLanguage(string criterion)
        => ContainsAny(criterion, "then", "must", "should", "returns", "response", "reject", "accept", "created", "updated", "deleted");
}
