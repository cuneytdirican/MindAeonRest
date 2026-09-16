using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Products;

public class IndexModel : PageModel
{
    private readonly ProductService _svc = new();

    public List<Product> Items { get; private set; } = new();
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
            all = all.Where(p => (p.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (p.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
        return Page();
    }

    /// <summary>Liste küçük resmi: ürünün ana (yoksa ilk) resmini döndürür.</summary>
    public IActionResult OnGetImage(int id)
    {
        if (!CanAccess) return Forbid();
        var bytes = _svc.GetMainImage(id);
        return bytes is { Length: > 0 } ? File(bytes, "image/png") : NotFound();
    }

    /// <summary>Soft-delete / geri al — WPF ile aynı jenerik mekanizma (Schema.SetDeleted, tablo "Products").</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "Products", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, ShowDeleted });
    }
}
