using Microsoft.AspNetCore.Mvc.RazorPages;
using GNex.Studio.ViewModels.Mpi;

namespace GNex.Studio.Pages.Mpi;

public class PatientDetailModel : PageModel
{
    public PatientDetailViewModel View { get; set; } = new();

    public async Task OnGetAsync(int page = 1, string? search = null)
    {
        View.CurrentPage = page;
        View.SearchTerm = search;
        // TODO: wire service injection
        await Task.CompletedTask;
    }
}