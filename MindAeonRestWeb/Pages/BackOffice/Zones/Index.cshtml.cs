using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Zones;

public class IndexModel : PageModel
{
    private readonly ZoneService _svc = new();

    public List<Zone> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.LocationTables);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var all = _svc.GetAll(includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(z => (z.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (z.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (z.BranchName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — firma DB (OpenCompany), jenerik Schema.SetDeleted.</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "Zones", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
