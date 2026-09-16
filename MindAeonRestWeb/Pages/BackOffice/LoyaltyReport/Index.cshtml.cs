using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.LoyaltyReport;

/// <summary>Puan / Sadakat Raporu — WPF LoyaltyReportWindow karşılığı.
/// Toplam puan yükümlülüğü (₺) + müşteri seçince puan bakiyesi + kazanım/harcama hareket ekstresi.
/// Salt-okunur. Sadakat AYARLARI (/BackOffice/Loyalty) ekranından ayrıdır.</summary>
public class IndexModel : PageModel
{
    private readonly LoyaltyService _svc = new();

    [BindProperty(SupportsGet = true)] public int CustomerId { get; set; }

    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public decimal TotalLiability { get; private set; }
    public bool HasCustomer => CustomerId > 0;
    public decimal Balance { get; private set; }
    public List<LoyaltyMovement> Movements { get; private set; } = new();

    // WPF: Admin | Permissions.Cash → burada admin/Patron. Feature gate yok.
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadCustomers();
        LoadLiability();

        if (CustomerId > 0)
        {
            Balance = _svc.GetBalance(CustomerId);
            Movements = _svc.GetMovements(CustomerId);
        }
        return Page();
    }

    private void LoadCustomers()
    {
        // WPF: PA (Peşin/kasa) carisi hariç.
        CustomerOptions = new() { new SelectListItem("(Müşteri seçiniz)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Where(c => c.Code != "PA")
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));
    }

    private void LoadLiability()
    {
        try { TotalLiability = _svc.GetTotalLiability(); }
        catch { TotalLiability = 0m; }
    }
}
