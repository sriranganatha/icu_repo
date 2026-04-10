using Microsoft.AspNetCore.Mvc.RazorPages;
using GNex.Studio.ViewModels.Emergency;

namespace GNex.Studio.Pages.Emergency;

public class EmergencyBoardModel : PageModel
{
    public EmergencyBoardViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}