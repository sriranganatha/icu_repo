namespace GNex.Studio.ViewModels.Clinical;

public sealed class EncounterListViewModel
{
    public List<EncounterRow> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SearchTerm { get; set; }
}

public sealed class EncounterRow
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Facility { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
}