using Microsoft.AspNetCore.Mvc.RazorPages;
using GNex.Studio.ViewModels.Inpatient;

namespace GNex.Studio.Pages.Inpatient;

public class AdmissionBoardModel : PageModel
{
    public AdmissionBoardViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}