using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.EInvoice;

/// <summary>e-Fatura / e-Arşiv Ayarları (tek kayıt, şirket geneli). WPF EInvoiceSettingsWindow ile
/// birebir alanlar: Sağlayıcı, Kullanıcı adı, Parola, Test ortamı, Modül etkin.
/// Gate: yalnız sistem yöneticisi (Session.IsSysAdmin) — WPF ile aynı. Feature kapısı yok.</summary>
public class IndexModel : PageModel
{
    private readonly EInvoiceService _svc = new();

    [BindProperty] public int Provider { get; set; }
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public bool IsTest { get; set; }
    [BindProperty] public bool IsEnabled { get; set; }

    public List<SelectListItem> ProviderOptions { get; private set; } = new();
    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    // WPF EInvoiceSettingsWindow: yalnız sistem yöneticisi.
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet(bool saved = false)
    {
        if (!CanAccess) return Forbid();
        LoadProviders();
        var s = _svc.GetSettings();
        Provider = (int)s.Provider;
        Username = s.Username;
        Password = s.Password;
        IsTest = s.IsTest;
        IsEnabled = s.IsEnabled;
        Saved = saved ? "e-Fatura ayarları kaydedildi." : null;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadProviders();
        try
        {
            var s = _svc.GetSettings(); // Id/audit alanlarını korumak için mevcudu yükle
            s.Provider = (EInvoiceProvider)Provider;
            s.Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
            s.Password = string.IsNullOrWhiteSpace(Password) ? null : Password.Trim();
            s.IsTest = IsTest;
            s.IsEnabled = IsEnabled;
            _svc.SaveSettings(s, Session.CurrentUserId);
            return RedirectToPage(new { saved = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadProviders()
        => ProviderOptions = Enum.GetValues<EInvoiceProvider>()
            .Select(p => new SelectListItem(p.ToTr(), ((int)p).ToString()))
            .ToList();
}
