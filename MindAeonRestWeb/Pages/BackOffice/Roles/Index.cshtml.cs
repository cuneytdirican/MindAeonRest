using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Roles;

public class IndexModel : PageModel
{
    private readonly RoleService _svc = new();

    public List<Role> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    // Roller SİSTEM DB'de ve yalnızca SysAdmin yönetir (WPF: "Yalnızca SysAdmin çağırmalı").
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var all = _svc.GetAll(includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(r => (r.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (r.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>AI Hub yetkisini aç/kapat — WPF'teki "🧠 AI Yetkisi Aç/Kapa" aksiyonuyla birebir.
    /// Güncel değeri DB'den okuyup tersine çevirir (RoleService.SetAiPermission).</summary>
    public IActionResult OnPostToggleAi(int id)
    {
        if (!CanAccess) return Forbid();
        var role = _svc.GetById(id, includeDeleted: true);
        if (role != null)
            _svc.SetAiPermission(id, !role.CanUseAi, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }

    /// <summary>Soft-delete / geri al — WPF ile aynı jenerik mekanizma (Schema.SetDeleted, SİSTEM DB "Roles").</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenConnection();
        Schema.SetDeleted(c, "Roles", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
