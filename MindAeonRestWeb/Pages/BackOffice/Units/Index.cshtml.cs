using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Units;

public class IndexModel : PageModel
{
    private readonly MainUnitService _svc = new();

    public List<MainUnit> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    // Birimler sistem DB'sinde (ortak) tutulur, lisans-feature yok — sadece rol denetimi.
    // Kiracı yöneticisi LİSTEYİ görür (ürün formları bu birimleri kullanır); ekle/düzenle/sil TÜM kiracıları
    // etkilediğinden yalnız SysAdmin (CanManage).
    private static bool CanAccess => Session.IsTenantAdmin;
    public bool CanManage => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var all = _svc.GetAll(includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(u => (u.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (u.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — sistem DB (OpenConnection), jenerik Schema.SetDeleted.</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanManage) return Forbid();   // ortak (global) veri → yalnız SysAdmin
        using var c = Database.OpenConnection();
        Schema.SetDeleted(c, "MainUnits", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
