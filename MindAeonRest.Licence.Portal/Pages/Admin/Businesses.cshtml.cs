using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class BusinessesModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<BusinessListItem> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Partial("_BusinessesTable", this);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var path = "/api/v1/businesses";
        if (!string.IsNullOrWhiteSpace(Q)) path += "?q=" + Uri.EscapeDataString(Q.Trim());
        Items = await _api.GetAsync<List<BusinessListItem>>(path, ct) ?? [];
    }
}
