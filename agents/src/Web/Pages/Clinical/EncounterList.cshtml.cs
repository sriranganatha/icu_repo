using Microsoft.AspNetCore.Mvc.RazorPages;
using GNex.Studio.ViewModels.Clinical;

namespace GNex.Studio.Pages.Clinical;

public class EncounterListModel : PageModel
{
    public EncounterListViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}