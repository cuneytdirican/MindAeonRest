using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Personnel;

public class IndexModel : PageModel
{
    private readonly PersonnelService _svc = new();

    public List<MindAeonRest.Models.Personnel> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    // Personel = Ön Muhasebe kapsamı — yalnız SysAdmin/Patron + PreAccounting lisansı.
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var all = _svc.GetAll(includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(p => (p.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (p.FullName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (p.Phone ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (p.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — WPF ile aynı jenerik mekanizma (Schema.SetDeleted).</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "Personnel", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
