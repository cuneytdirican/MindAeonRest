using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class UsageModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<UsageItem> Items { get; private set; } = [];

    public decimal TotalRevenue => Items.Sum(u => u.Revenue);
    public int TotalBranches => Items.Sum(u => u.BranchCount);
    public int TotalKiosks => Items.Sum(u => u.KioskCount);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<UsageItem>>("/api/v1/usage", ct) ?? [];
    }
}
