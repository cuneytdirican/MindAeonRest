using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Branches;

public class IndexModel : PageModel
{
    private readonly BranchService _svc = new();

    public List<CompanyBranch> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    private static bool CanAccess => Session.IsTenantAdmin;

    /// <summary>
    /// Web'de Session.CurrentCompany.Id dolu değildir (TenantMiddleware yalnızca Code/DatabaseName kurar).
    /// AuthController.MyBranches deseniyle Company.Id'yi koddan çözeriz.
    /// </summary>
    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var companyId = ResolveCompanyId();
        if (companyId is null) return Page();

        var all = _svc.GetByCompany(companyId.Value, includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(b => (b.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (b.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — Şubeler SİSTEM DB'sindedir (Database.OpenConnection).
    /// KİRACI İZOLASYONU: id POST'tan gelir → şubenin AKTİF şirkete ait olduğu doğrulanmadan işlem yapılmaz
    /// (aksi halde başka şirketin şubesi id ile silinebilirdi — IDOR). Silinmiş şube de (geri al) kapsanır.</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        var companyId = ResolveCompanyId();
        if (companyId is null || !_svc.GetByCompany(companyId.Value, includeDeleted: true).Any(b => b.Id == id))
            return Forbid();
        using var c = Database.OpenConnection();
        Schema.SetDeleted(c, "CompanyBranches", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
