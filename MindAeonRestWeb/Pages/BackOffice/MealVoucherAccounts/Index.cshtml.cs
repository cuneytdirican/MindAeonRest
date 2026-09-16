using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.MealVoucherAccounts;

public class IndexModel : PageModel
{
    private readonly MealVoucherAccountService _svc = new();

    public List<MealVoucherAccount> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    // Core özellik (lisans kapısı yok) — yalnız SysAdmin/Patron.
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var all = _svc.GetAll(includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(a => (a.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (a.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (a.BranchName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (a.MealVoucherName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — WPF ile aynı jenerik mekanizma (Schema.SetDeleted, şirket DB "MealVoucherAccounts").</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "MealVoucherAccounts", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
