namespace HmsAgents.Core.Models;

/// <summary>
/// INVEST readiness score for a single requirement.
/// </summary>
public sealed class RequirementQualityScore
{
    public string RequirementId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Score { get; init; }
    public bool Independent { get; init; }
    public bool Negotiable { get; init; }
    public bool Valuable { get; init; }
    public bool Estimable { get; init; }
    public bool Small { get; init; }
    public bool Testable { get; init; }
    public bool IsReady { get; init; }
    public List<string> Notes { get; init; } = [];
    public List<string> ClarifyingQuestions { get; init; } = [];
}
