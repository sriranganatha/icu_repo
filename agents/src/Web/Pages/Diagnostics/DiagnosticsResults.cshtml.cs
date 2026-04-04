using Microsoft.AspNetCore.Mvc.RazorPages;
using Hms.Web.ViewModels.Diagnostics;

namespace Hms.Web.Pages.Diagnostics;

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