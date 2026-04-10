using Microsoft.AspNetCore.Mvc.RazorPages;
using GNex.Studio.ViewModels.Governance;

namespace GNex.Studio.Pages.Governance;

public class AuditLogModel : PageModel
{
    public AuditLogViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}