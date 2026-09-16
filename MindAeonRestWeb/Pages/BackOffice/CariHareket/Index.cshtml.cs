using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.CariHareket;

/// <summary>Cari Hareketler (ekstre) — WPF Cari ekstre + CariHareketEditWindow karşılığı.
/// Cari + tarih aralığı filtresi → o carinin dönem hareketleri (devir + yürüyen bakiye).</summary>
public class IndexModel : PageModel
{
    private readonly CariHareketService _svc = new();

    [BindProperty(SupportsGet = true)] public int CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public decimal Opening { get; private set; }
    public List<MindAeonRest.Models.CariHareket> Rows { get; private set; } = new();
    public decimal Closing { get; private set; }

    // Ekstre footer'ı için gerçek borç/alacak toplamları (WPF paritesi).
    public decimal TotalDebt { get; private set; }
    public decimal TotalCredit { get; private set; }

    // Cari SEÇİLİ DEĞİLKEN: tüm carilerin son hareketleri (WPF MainWindow CariHareketGrid = GetRecent).
    public List<MindAeonRest.Models.CariHareket> RecentRows { get; private set; } = new();
    private const int RecentTake = 300;

    // Ön Muhasebe kapısı — yalnız SysAdmin/Patron + Feature.PreAccounting.
    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadCustomers();

        From ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        To ??= DateTime.Today;

        if (CustomerId > 0)
        {
            var (opening, rows) = _svc.GetExtract(CustomerId, From.Value, To.Value);
            Opening = opening;
            Rows = rows;
            Closing = rows.Count > 0 ? rows[^1].RunningBalance : opening;
            TotalDebt = rows.Sum(r => r.Debt);
            TotalCredit = rows.Sum(r => r.Credit);
        }
        else
        {
            // Cari seçilmemiş: tüm carilerin son hareketleri (WPF MainWindow.CariHareketGrid).
            RecentRows = _svc.GetRecent(RecentTake);
        }
        return Page();
    }

    private void LoadCustomers()
    {
        CustomerOptions = new() { new SelectListItem("(Cari seçiniz)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));
    }
}
