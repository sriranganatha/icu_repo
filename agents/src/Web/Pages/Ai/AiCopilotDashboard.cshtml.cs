using Microsoft.AspNetCore.Mvc.RazorPages;
using Hms.Web.ViewModels.Ai;

namespace Hms.Web.Pages.Ai;

public class AiCopilotDashboardModel : PageModel
{
    public AiCopilotDashboardViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}