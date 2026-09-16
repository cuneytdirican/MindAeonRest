using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Categories;

public class IndexModel : PageModel
{
    private readonly ProductCategoryService _svc = new();

    public List<ProductCategory> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        var all = _svc.GetAll(includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(c => (c.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (c.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Soft-delete / geri al — WPF ile aynı jenerik mekanizma (Schema.SetDeleted).</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "ProductCategories", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }

    /// <summary>Kategori resmi (liste/edit önizleme).</summary>
    public IActionResult OnGetImage(int id)
    {
        if (!CanAccess) return Forbid();
        var bytes = _svc.GetImage(id);
        return bytes is { Length: > 0 } ? File(bytes, "image/png") : NotFound();
    }
}
