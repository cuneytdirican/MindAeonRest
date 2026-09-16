using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Expenses;

/// <summary>Giderler listesi — WPF GiderListWindow karşılığı.
/// Tarih aralığı filtresi → dönem giderleri (tarih/gider tipi/tutar/ödeme/açıklama) + toplam.</summary>
public class IndexModel : PageModel
{
    private readonly ExpenseService _svc = new();

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<Expense> Items { get; private set; } = new();
    public decimal Total { get; private set; }

    // Ön Muhasebe kapısı — yalnız SysAdmin/Patron + Feature.PreAccounting.
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        From ??= DateTime.Today.AddMonths(-1);
        To ??= DateTime.Today;

        Items = _svc.GetList(From, To);
        Total = Items.Sum(x => x.Amount);
        return Page();
    }
}
