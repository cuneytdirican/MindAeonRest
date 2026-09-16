using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.Loyalty;

/// <summary>Puan/sadakat programı ayarları (tek form): aktiflik, kazanım oranı (₺ başına puan),
/// harcama oranı (1 puanın ₺ karşılığı). WPF LoyaltySettingsWindow ile birebir alanlar.</summary>
public class IndexModel : PageModel
{
    private readonly LoyaltyService _svc = new();
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    [BindProperty] public bool IsActive { get; set; }
    // Kültürden bağımsız ayrıştırma için oranları string olarak bağlıyoruz (virgül/nokta kabul).
    [BindProperty] public string EarnRate { get; set; } = "";
    [BindProperty] public string RedeemRate { get; set; } = "";

    public string? Error { get; private set; }
    public string? Saved { get; private set; }
    public decimal TotalLiability { get; private set; }

    // WPF: Admin | Permissions.Cash — burada admin/Patron yeterli. Feature gate yok.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    public IActionResult OnGet(bool saved = false)
    {
        if (!CanAccess) return Forbid();
        var s = _svc.GetSettings();
        IsActive = s.IsActive;
        EarnRate = s.EarnRate.ToString("0.####", Tr);
        RedeemRate = s.RedeemRate.ToString("0.####", Tr);
        Saved = saved ? "Sadakat ayarları kaydedildi." : null;
        LoadLiability();
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLiability();

        if (!TryParseRate(EarnRate, out var earn) || earn < 0)
        {
            Error = "Kazanım oranı geçersiz (negatif olamaz).";
            return Page();
        }
        if (!TryParseRate(RedeemRate, out var redeem) || redeem <= 0)
        {
            Error = "Harcama oranı (1 puanın ₺ karşılığı) 0'dan büyük olmalı.";
            return Page();
        }

        try
        {
            _svc.SaveSettings(new LoyaltySettings { IsActive = IsActive, EarnRate = earn, RedeemRate = redeem }, Session.CurrentUserId);
            return RedirectToPage(new { saved = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadLiability()
    {
        try { TotalLiability = _svc.GetTotalLiability(); }
        catch { TotalLiability = 0m; }
    }

    private static bool TryParseRate(string? text, out decimal value) =>
        Num.TryParseDecimal(text, out value);
}
