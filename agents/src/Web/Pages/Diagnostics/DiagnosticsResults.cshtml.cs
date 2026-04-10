using Microsoft.AspNetCore.Mvc.RazorPages;
using GNex.Studio.ViewModels.Diagnostics;

namespace GNex.Studio.Pages.Diagnostics;

public class DiagnosticsResultsModel : PageModel
{
    public DiagnosticsResultsViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}