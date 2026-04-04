using Microsoft.AspNetCore.Mvc.RazorPages;
using Hms.Web.ViewModels.Clinical;

namespace Hms.Web.Pages.Clinical;

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