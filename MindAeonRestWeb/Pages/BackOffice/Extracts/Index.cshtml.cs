using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Extracts;

/// <summary>Hesap Ekstreleri (RAPOR) — WPF <c>ExtractWindow</c> birebir karşılığı.
/// 4 tip (0=Nakit Kasa, 1=POS, 2=Banka, 3=Yemek Çeki) + hesap + tarih aralığı →
/// yürüyen bakiyeli hareket dökümü. Salt-okunur; hiçbir yazma yok. Yazdır = window.print.
/// Ekstre üretimi Core <c>ExtractService</c> (WPF ile aynı motor); hesap listeleri ilgili servisler.</summary>
public class IndexModel : PageModel
{
    // WPF AccType: 0=Nakit Kasa, 1=POS, 2=Banka, 3=Yemek Çeki
    public const int TypeCash = 0, TypePos = 1, TypeBank = 2, TypeVoucher = 3;

    private readonly ExtractService _extract = new();
    private readonly BranchService _branch = new();

    // ── Filtre ──
    [BindProperty(SupportsGet = true)] public int Type { get; set; } = TypeCash;
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? AccountId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<SelectListItem> TypeOptions { get; private set; } = new();
    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<SelectListItem> AccountOptions { get; private set; } = new();

    // Banka hesapları şube bağımsız → şube filtresini gizle (WPF BranchPanel.Visibility)
    public bool ShowBranch => Type != TypeBank;

    // ── Sonuç ──
    public ExtractResult? Result { get; private set; }
    public string? Title { get; private set; }
    public string? Subtitle { get; private set; }
    public string? Error { get; private set; }

    // Gate: yönetici/Patron + lisans (PreAccounting) — Finans/Ön-muhasebe ekran deseni.
    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        return Page();
    }

    /// <summary>Filtre seçenekleri + varsayılanlar (WPF ctor + Loaded).</summary>
    private void Prepare()
    {
        TypeOptions = new()
        {
            new("💵 Nakit Kasa", TypeCash.ToString()),
            new("💳 POS / Kredi Kartı", TypePos.ToString()),
            new("🏦 Banka Hesabı", TypeBank.ToString()),
            new("🎫 Yemek Çeki", TypeVoucher.ToString()),
        };

        var co = Session.CurrentCompany;
        var branches = co is null ? new List<CompanyBranch>() : _branch.GetByCompany(co.Id).ToList();
        BranchOptions = branches.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();
        BranchId ??= Session.CurrentBranch?.Id ?? branches.FirstOrDefault()?.Id;

        // WPF varsayılan aralık: son 30 gün → bugün.
        From ??= DateTime.Today.AddDays(-30);
        To ??= DateTime.Today;

        // Hesap listesi tipe göre (WPF LoadAccounts) — banka şube-bağımsız.
        AccountOptions = Type switch
        {
            TypeCash => new CashRegisterService().GetAll()
                .Where(x => x.IsActive && x.BranchId == BranchId)
                .Select(x => new SelectListItem($"{x.Name} ({x.ScopeText})", x.Id.ToString())).ToList(),
            TypePos => new PosService().GetAll()
                .Where(x => x.IsActive && x.BranchId == BranchId)
                .Select(x => new SelectListItem($"{x.Name} ({x.ScopeText})", x.Id.ToString())).ToList(),
            TypeBank => new BankAccountService().GetAll()
                .Where(x => x.IsActive)
                .Select(x => new SelectListItem(x.Display, x.Id.ToString())).ToList(),
            TypeVoucher => new MealVoucherAccountService().GetAll()
                .Where(x => x.IsActive && x.BranchId == BranchId)
                .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToList(),
            _ => new List<SelectListItem>()
        };

        // Seçili hesap listede yoksa (tip/şube değişti) → ilkine düş (WPF SelectedIndex=0).
        if (AccountOptions.Count == 0) AccountId = null;
        else if (AccountId is null || AccountOptions.All(o => o.Value != AccountId.Value.ToString()))
            AccountId = int.Parse(AccountOptions[0].Value);
    }

    /// <summary>Ekstreyi üret (WPF Load). Hesap seçili değilse boş.</summary>
    private void Build()
    {
        if (AccountId is not int accId) return;
        try
        {
            var from = (From ?? DateTime.Today).Date;
            var to = (To ?? DateTime.Today).Date;
            if (to < from) (from, to) = (to, from);

            Result = Type switch
            {
                TypeCash => _extract.GetCashExtract(accId, from, to),
                TypePos => _extract.GetPosExtract(accId, from, to),
                TypeBank => _extract.GetBankExtract(accId, from, to),
                TypeVoucher => _extract.GetVoucherExtract(accId, from, to),
                _ => new ExtractResult()
            };

            var typeText = TypeOptions.FirstOrDefault(o => o.Value == Type.ToString())?.Text ?? "";
            var accText = AccountOptions.FirstOrDefault(o => o.Value == accId.ToString())?.Text ?? "";
            Title = $"📒 {typeText} Ekstresi — {accText}";
            Subtitle = $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}  ·  Açılış: {Money(Result.Opening)}  ·  Kapanış: {Money(Result.Closing)}";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public static string Money(decimal v) => v.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
