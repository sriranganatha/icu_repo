using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GNex.Studio.Pages;

public class IndexModel : PageModel
{
    public void OnGet() { }
}

public class AgentCardModel
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-cpu";
    public string AgentKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
