using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Users;

public class IndexModel : PageModel
{
    private readonly UserService _svc = new();

    public List<User> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    // Kullanıcı yönetimi SİSTEM DB'dir; yalnız SysAdmin.
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        // Şirket bağlamı: seçili şirketin sistem-DB Id'si (şube erişim özeti için).
        int companyId = ResolveCompanyId();

        var all = companyId > 0
            ? _svc.GetForCompany(companyId, includeDeleted: ShowDeleted)
            : _svc.GetAll(); // şirket seçili değilse (nadir) tüm aktif kullanıcılar

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(u => (u.Username ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (u.FullName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (u.RolesText ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — sistem DB, tablo "Users" (WPF ile aynı jenerik mekanizma).</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenConnection();   // SİSTEM DB
        Schema.SetDeleted(c, "Users", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }

    private static int ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrWhiteSpace(code)) return 0;
        return new CompanyService().GetByCode(code)?.Id ?? 0;
    }
}
