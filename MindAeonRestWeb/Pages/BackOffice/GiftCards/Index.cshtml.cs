using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.GiftCards;

public class IndexModel : PageModel
{
    private readonly GiftCardService _svc = new();

    public List<GiftCard> Items { get; private set; } = new();
    public decimal TotalLiability { get; private set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowInactive { get; set; }

    // Core özellik (lisans kapısı yok) — yalnız SysAdmin/Patron (WPF Permissions.Cash alt kümesi).
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Load();
        return Page();
    }

    /// <summary>Aktif hediye çekini iptal eder (WPF CancelCard_Click karşılığı; kalan bakiye silinir).</summary>
    public IActionResult OnPostCancel(int id)
    {
        if (!CanAccess) return Forbid();
        try
        {
            _svc.Cancel(id, Session.CurrentUserId);
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage(new { Q, ShowInactive });
    }

    private void Load()
    {
        var all = _svc.GetAll(includeInactive: ShowInactive);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(g => (g.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (g.CustomerName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        TotalLiability = _svc.GetTotalLiability();
    }
}
