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
    /// <summary>Sil/geri al PRG hatası (ör. lisans kullanıcı limiti).</summary>
    public string? Error { get; private set; }

    // Kullanıcı yönetimi SİSTEM DB'dir. SysAdmin: tüm kullanıcılar (eski davranış).
    // Kiracı yöneticisi (Patron/Yönetici): YALNIZ aktif şirkete erişimi olan, SysAdmin OLMAYAN kullanıcılar.
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Error = TempData["UsersError"] as string;

        // Şirket bağlamı: seçili şirketin sistem-DB Id'si (şube erişim özeti için).
        int companyId = ResolveCompanyId();

        List<User> all;
        if (Session.IsSysAdmin)
            all = companyId > 0
                ? _svc.GetForCompany(companyId, includeDeleted: ShowDeleted)
                : _svc.GetAll(); // şirket seçili değilse (nadir) tüm aktif kullanıcılar
        else
        {
            // KİRACI İZOLASYONU: GetForCompany TÜM kullanıcıları döndürür → erişim kaydı olmayanlar (başka kiracılar)
            // ve SysAdmin'ler (global) kiracı yöneticisine gösterilmez.
            if (companyId <= 0) return Page();
            var own = new UserAccessService().GetUserIdsForCompany(companyId);
            all = _svc.GetForCompany(companyId, includeDeleted: ShowDeleted)
                .Where(u => own.Contains(u.Id) && !IsSysAdminUser(u)).ToList();
        }

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

    /// <summary>Soft-delete / geri al — sistem DB, tablo "Users" (WPF ile aynı jenerik mekanizma).
    /// Kiracı yöneticisi: hedef aktif şirkete erişimli, SysAdmin değil, başka şirkete bağlı değil olmalı; kendini silemez;
    /// geri alma lisans kullanıcı limitine tabidir.</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        if (!Session.IsSysAdmin)
        {
            int companyId = ResolveCompanyId();
            if (!EditModel.IsManageableByTenantAdmin(id, companyId)) return Forbid();
            if (deleted && id == Session.CurrentUserId) return Forbid();   // kendini silip şirketi kilitleme engeli
            if (!deleted)
            {
                try { UserService.EnforceUserLimit(companyId); }
                catch (InvalidOperationException ex) { TempData["UsersError"] = ex.Message; return RedirectToPage(new { Q, ShowDeleted }); }
            }
        }
        using var c = Database.OpenConnection();   // SİSTEM DB
        Schema.SetDeleted(c, "Users", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }

    private static bool IsSysAdminUser(User u) =>
        string.Equals(u.RoleName, "SysAdmin", StringComparison.OrdinalIgnoreCase)
        || (u.Roles?.Any(r => string.Equals(r, "SysAdmin", StringComparison.OrdinalIgnoreCase)) ?? false);

    private static int ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrWhiteSpace(code)) return 0;
        return new CompanyService().GetByCode(code)?.Id ?? 0;
    }
}
