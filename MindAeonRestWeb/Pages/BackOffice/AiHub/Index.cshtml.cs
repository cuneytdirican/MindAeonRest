using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services.Ai;

namespace MindAeonRestWeb.Pages.BackOffice.AiHub;

/// <summary>
/// AI Hub — WPF AiHubWindow + AiSettingsWindow'un web karşılığı (tek sayfa).
/// (1) AI Ayarları formu: sağlayıcı, API anahtarı, model, BaseUrl, yaratıcılık (temperature), sesli çıkış/giriş.
/// (2) Sorgu kutusu → AiHubService.Ask(soru) → deterministik salt-okuma araçlarla metin + opsiyonel tablo yanıtı.
/// Gate: Permissions.Ai (rol CanUseAi bit'i; SysAdmin bypass) + Feature.AiHub. Sesli giriş (STT) web'de yok — not olarak işaretli.
/// </summary>
public class IndexModel : PageModel
{
    private readonly AiSettingsService _settings = new();

    // --- Ayar alanları (AiSettingsWindow ile birebir) ---
    [BindProperty] public int Provider { get; set; }
    [BindProperty] public string? ApiKey { get; set; }
    [BindProperty] public string? Model { get; set; }
    [BindProperty] public string? BaseUrl { get; set; }
    [BindProperty] public double Temperature { get; set; } = 0.2;
    [BindProperty] public bool VoiceOutput { get; set; } = true;
    [BindProperty] public bool VoiceInput { get; set; }

    // --- Sorgu ---
    [BindProperty] public string? Question { get; set; }

    public string? AnswerText { get; private set; }
    public ReportTable? AnswerTable { get; private set; }

    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    public List<(int Value, string Text)> ProviderOptions { get; } = new()
    {
        ((int)AiProviderType.Local,  AiProviderType.Local.ToTr()),
        ((int)AiProviderType.Claude, AiProviderType.Claude.ToTr()),
        ((int)AiProviderType.OpenAI, AiProviderType.OpenAI.ToTr()),
        ((int)AiProviderType.Custom, AiProviderType.Custom.ToTr()),
    };

    public List<AiPreset> Presets => AiPreset.All;

    // WPF paritesi: rol bazlı CanUseAi bit'i (SysAdmin bypass + 'Yönetici' vb. yetkili roller) + lisans.
    private static bool CanAccess =>
        Permissions.Ai && Session.LicenseHas(Feature.AiHub);

    public IActionResult OnGet(bool saved = false)
    {
        if (!CanAccess) return Forbid();
        LoadSettings();
        Saved = saved ? "AI ayarları kaydedildi." : null;
        return Page();
    }

    /// <summary>AI sağlayıcı ayarlarını kaydet (AiSettingsWindow.Save_Click).</summary>
    public IActionResult OnPostSave()
    {
        if (!CanAccess) return Forbid();

        var provider = (AiProviderType)Provider;
        if (provider == AiProviderType.Custom && string.IsNullOrWhiteSpace(BaseUrl))
        {
            Error = "Özel sağlayıcı için uç nokta (BaseUrl) gerekli.";
            return Page();
        }

        var s = new AiSettings
        {
            Provider = provider,
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
            Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl.Trim(),
            Temperature = Temperature,
            VoiceOutput = VoiceOutput,
            VoiceInput = VoiceInput,
        };

        try
        {
            _settings.Save(s, Session.CurrentUserId);
            return RedirectToPage(new { saved = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>Soruyu AI Hub servisine gönder, metin + opsiyonel tablo yanıtı üret (AiHubWindow.Send).</summary>
    public IActionResult OnPostAsk()
    {
        if (!CanAccess) return Forbid();
        LoadSettings();

        var q = (Question ?? "").Trim();
        if (q.Length == 0)
        {
            Error = "Lütfen bir soru yazın.";
            return Page();
        }

        try
        {
            AiAnswer ans = new AiHubService().Ask(q);
            AnswerText = ans.Text;
            AnswerTable = ans.Table;
        }
        catch (Exception ex)
        {
            Error = "Hata: " + ex.Message;
        }
        return Page();
    }

    private void LoadSettings()
    {
        try
        {
            var s = _settings.Get();
            Provider = (int)s.Provider;
            ApiKey = s.ApiKey;
            Model = s.Model;
            BaseUrl = s.BaseUrl;
            Temperature = s.Temperature;
            VoiceOutput = s.VoiceOutput;
            VoiceInput = s.VoiceInput;
        }
        catch (Exception ex)
        {
            // Ayar okunamazsa varsayılan (Yerel) alanlarla göster — ama sessizce yutma, izini bırak.
            System.Diagnostics.Debug.WriteLine($"[AiHub] LoadSettings başarısız: {ex.Message}");
        }
    }
}
