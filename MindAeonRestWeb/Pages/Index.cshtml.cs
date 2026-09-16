using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRestWeb.Services;

namespace MindAeonRestWeb.Pages;

public class IndexModel(LicenceCatalogClient catalog) : PageModel
{
    public LicenceCatalogClient.PricingView Pricing { get; private set; } =
        new(new(), new(), "TRY", false);

    public async Task OnGetAsync()
    {
        Pricing = await catalog.GetAsync();
    }
}
