using Microsoft.AspNetCore.Mvc.RazorPages;
using Hms.Web.ViewModels.Revenue;

namespace Hms.Web.Pages.Revenue;

public class BillingDashboardModel : PageModel
{
    public BillingDashboardViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}