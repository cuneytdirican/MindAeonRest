using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.Dekont;

/// <summary>
/// Tek işlem dekont formu (Kasa / Banka / Kredi Kartı giriş-çıkış).
/// WPF DekontWindow ile birebir aynı servis çağrıları:
///   Kasa  → CashMovementService (cari varsa AddCari, yoksa AddManual)
///   Banka → BankMovementService.AddManual (cari varsa Tahsilat/Odeme, yoksa Giris/Cikis)
///   Kart  → PosMovementService.AddManual (Tahsilat/Odeme, cari zorunlu)
/// </summary>
public class IndexModel : PageModel
{
    // WPF DekontKind ile aynı sıra/anlam.
    public enum DekontKind { KasaGiris = 0, KasaCikis = 1, BankaGiris = 2, BankaCikis = 3, KartTahsilat = 4, KartOdeme = 5 }

    [BindProperty] public int Kind { get; set; }
    [BindProperty] public int InstrumentId { get; set; }   // seçilen enstrümanın (kasa/banka/POS) Id'si
    [BindProperty] public int CustomerId { get; set; }     // 0 = seçilmedi
    [BindProperty] public string Amount { get; set; } = "";
    [BindProperty] public int CurrencyId { get; set; }
    [BindProperty] public DateTime? Date { get; set; }
    [BindProperty] public string? Note { get; set; }

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public List<SelectListItem> KindOptions { get; private set; } = new();
    public List<SelectListItem> CurrencyOptions { get; private set; } = new();
    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public List<SelectListItem> CashOptions { get; private set; } = new();
    public List<SelectListItem> BankOptions { get; private set; } = new();
    public List<SelectListItem> PosOptions { get; private set; } = new();

    // Gate: admin/Patron + PreAccounting (WPF Permissions.Cash; PreAccounting yeterli).
    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    private static DekontKind K(int v) => (DekontKind)v;
    private static bool IsIn(DekontKind k) => k is DekontKind.KasaGiris or DekontKind.BankaGiris or DekontKind.KartTahsilat;
    private static bool IsCash(DekontKind k) => k is DekontKind.KasaGiris or DekontKind.KasaCikis;
    private static bool IsBank(DekontKind k) => k is DekontKind.BankaGiris or DekontKind.BankaCikis;
    private static bool IsCard(DekontKind k) => k is DekontKind.KartTahsilat or DekontKind.KartOdeme;

    public IActionResult OnGet(bool ok = false)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        Kind = (int)DekontKind.KasaGiris;
        CurrencyId = new CurrencyService().GetDefaultId() ?? 0;
        Date = DateTime.Today;
        if (ok) Success = "Dekont kaydedildi.";
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        var kind = K(Kind);

        if (InstrumentId == 0)
        {
            Error = IsCash(kind) ? "Kasa seçiniz."
                  : IsBank(kind) ? "Banka hesabı seçiniz."
                  : "POS cihazı seçiniz.";
            return Page();
        }

        if (!Num.TryParseDecimal(Amount, out var amount) || amount <= 0)
        {
            Error = "Geçerli bir tutar giriniz (0'dan büyük).";
            return Page();
        }

        int? customerId = CustomerId > 0 ? CustomerId : null;
        if (IsCard(kind) && customerId is null)
        {
            Error = "Kredi kartı tahsilat/ödeme için cari (müşteri) seçiniz.";
            return Page();
        }

        string? note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        bool isIn = IsIn(kind);

        try
        {
            if (IsCash(kind))
            {
                // Kasa: cari varsa AddCari (Tahsilat/Ödeme + cari hareket), yoksa manuel Giris/Cikis.
                if (customerId is int cc)
                    new CashMovementService().AddCari(InstrumentId, isIn, amount, cc, note, Session.CurrentUserId);
                else
                    new CashMovementService().AddManual(InstrumentId, isIn ? CashMovementType.Giris : CashMovementType.Cikis, amount, note, Session.CurrentUserId);
            }
            else if (IsBank(kind))
            {
                // Banka: cari varsa Tahsilat/Odeme (cari hareket), yoksa Giris/Cikis.
                var type = customerId is not null
                    ? (isIn ? BankMovementType.Tahsilat : BankMovementType.Odeme)
                    : (isIn ? BankMovementType.Giris : BankMovementType.Cikis);
                new BankMovementService().AddManual(InstrumentId, type, amount, customerId, note, Session.CurrentUserId);
            }
            else // kart (POS)
            {
                new PosMovementService().AddManual(InstrumentId, isIn ? BankMovementType.Tahsilat : BankMovementType.Odeme, amount, customerId, note, Session.CurrentUserId);
            }

            // PRG: başarı mesajını sonraki GET'te göster.
            return RedirectToPage(new { ok = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadLookups()
    {
        int branch = Session.CurrentBranch?.Id ?? 0;

        KindOptions = new()
        {
            new SelectListItem("Nakit Kasa — Giriş (Tahsilat)",  ((int)DekontKind.KasaGiris).ToString()),
            new SelectListItem("Nakit Kasa — Çıkış (Ödeme)",     ((int)DekontKind.KasaCikis).ToString()),
            new SelectListItem("Banka — Giriş (Tahsilat)",       ((int)DekontKind.BankaGiris).ToString()),
            new SelectListItem("Banka — Çıkış (Ödeme)",          ((int)DekontKind.BankaCikis).ToString()),
            new SelectListItem("Kredi Kartı — Tahsilat",         ((int)DekontKind.KartTahsilat).ToString()),
            new SelectListItem("Kredi Kartı — Ödeme",            ((int)DekontKind.KartOdeme).ToString()),
        };

        CurrencyOptions = new CurrencyService().GetAll()
            .Select(c => new SelectListItem(c.Code, c.Id.ToString()))
            .ToList();

        CustomerOptions = new() { new SelectListItem("(Müşteri seçiniz — opsiyonel)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Where(c => c.Code != "PA")
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));

        // Kasa & POS: aktif + şube. Banka: aktif (WPF ile aynı; banka şubeye bağlı değil).
        CashOptions = new CashRegisterService().GetAll()
            .Where(k => k.IsActive && k.BranchId == branch)
            .Select(k => new SelectListItem($"{k.Code} · {k.Name}", k.Id.ToString()))
            .ToList();

        BankOptions = new BankAccountService().GetAll()
            .Where(b => b.IsActive)
            .Select(b => new SelectListItem(b.Display, b.Id.ToString()))
            .ToList();

        PosOptions = new PosService().GetAll()
            .Where(p => p.IsActive && p.BranchId == branch)
            .Select(p => new SelectListItem($"{p.Code} · {p.Name}", p.Id.ToString()))
            .ToList();
    }
}
