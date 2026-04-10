namespace GNex.Studio.ViewModels.Ai;

public sealed class AiCopilotDashboardViewModel
{
    public List<AiInteractionRow> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SearchTerm { get; set; }
}

public sealed class AiInteractionRow
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Facility { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
}