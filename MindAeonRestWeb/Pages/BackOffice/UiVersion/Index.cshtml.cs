using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.UiVersion;

/// <summary>Ekran Sürümü ayarı (tek form): Adisyon ve Mutfak (KDS) ekranlarının sürümü
/// (Varsayılan / V2). WPF UiVersionSettingsWindow ile birebir alanlar. Şirket DB, tek satır (Id=1).
/// Gate: yalnız yönetici (Session.IsSysAdmin). Feature gate yok.</summary>
public class IndexModel : PageModel
{
    private readonly CompanyUiSettingsService _svc = new();

    // Tag değerleri UiVersions sabitleriyle (Varsayilan / V2) birebir.
    [BindProperty] public string AdisyonVersion { get; set; } = UiVersions.Default;
    [BindProperty] public string MutfakVersion { get; set; } = UiVersions.Default;

    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    // WPF: Session.IsSysAdmin. Web'de de yalnız yönetici.
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet(bool saved = false)
    {
        if (!CanAccess) return Forbid();
        var s = _svc.Get();
        AdisyonVersion = UiVersions.Normalize(s.AdisyonVersion);
        MutfakVersion = UiVersions.Normalize(s.MutfakVersion);
        Saved = saved ? "Ekran sürümü kaydedildi. Ekranlar bir sonraki açılışta yeni sürümle gelir." : null;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();

        try
        {
            _svc.Save(new CompanyUiSettings
            {
                AdisyonVersion = UiVersions.Normalize(AdisyonVersion),
                MutfakVersion = UiVersions.Normalize(MutfakVersion)
            }, Session.CurrentUserId);
            return RedirectToPage(new { saved = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
