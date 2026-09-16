using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Helpers;
using MindAeonRest.Services;
using MindAeonRestWeb.Services;
using QRCoder;

namespace MindAeonRestWeb.Pages.BackOffice.Security;

/// <summary>
/// 2FA (TOTP) KAYIT ekranı — giriş yapmış kullanıcının KENDİ hesabı için (self-service).
/// Hedef her zaman <see cref="Session.CurrentUserId"/>'dir; başka kullanıcı id'si parametre olarak ALINMAZ
/// (yetki yükselmesi yok). Etkinleştir → secret üret + QR göster → kod doğrula → TwoFactorEnabled=1.
/// Devre dışı bırak → geçerli kod ile. Secret master-seviye IDataProtector ile şifreli saklanır.
///
/// <para>IP bazlı "login" rate-limit uygulanır (OnPostDisable dahil tüm handler'lar) — kullanıcı bazlı
/// deneme kilidine (TwoFactorManager.VerifyLogin) EK kaba-kuvvet katmanı.</para>
/// </summary>
[EnableRateLimiting("login")]
public class TwoFactorModel(TwoFactorManager twoFactor) : PageModel
{
    private const string PendingKey = "2fa_enroll_pending";   // bekleyen şifreli secret (TempData/DataProtection)

    [BindProperty] public string? Code { get; set; }

    public bool Enabled { get; private set; }
    public bool Enrolling { get; private set; }
    public string? QrDataUri { get; private set; }
    public string? ManualKey { get; private set; }
    public string? StatusMessage { get; private set; }
    public string? Error { get; private set; }

    private int UserId => Session.CurrentUserId ?? 0;

    private string Account()
    {
        var u = new UserService().GetById(UserId);
        return u?.Username ?? User.Identity?.Name ?? $"user{UserId}";
    }

    public IActionResult OnGet()
    {
        if (UserId == 0) return RedirectToPage("/Login");
        Enabled = twoFactor.IsEnabled(UserId);
        return Page();
    }

    /// <summary>"2FA Etkinleştir": yeni secret + QR üret; bekleyen (şifreli) secret'ı onaya kadar TempData'da tut.</summary>
    public IActionResult OnPostBegin()
    {
        if (UserId == 0) return RedirectToPage("/Login");
        if (twoFactor.IsEnabled(UserId)) { Enabled = true; return Page(); }

        var (secretBase32, protectedSecret, uri) = twoFactor.BeginEnrollment(Account());
        TempData[PendingKey] = protectedSecret;
        ShowEnrollment(secretBase32, uri);
        return Page();
    }

    /// <summary>Kayıt onayı: TempData'daki bekleyen secret'a karşı kodu doğrula; geçerliyse yaz + etkinleştir.</summary>
    public IActionResult OnPostConfirm()
    {
        if (UserId == 0) return RedirectToPage("/Login");

        var pending = TempData.Peek(PendingKey) as string;
        if (string.IsNullOrEmpty(pending))
        {
            Error = "Kayıt oturumu sona erdi. Lütfen yeniden başlatın.";
            Enabled = twoFactor.IsEnabled(UserId);
            return Page();
        }

        if (twoFactor.ConfirmEnrollment(UserId, pending, Code))
        {
            TempData.Remove(PendingKey);
            Enabled = true;
            StatusMessage = "İki adımlı doğrulama etkinleştirildi.";
            return Page();
        }

        // Hatalı kod → QR'ı bekleyen secret'tan yeniden çizip formu koru.
        Error = "Kod doğrulanamadı. Uygulamadaki güncel 6 haneli kodu girin.";
        var desc = twoFactor.DescribeProtected(pending, Account());
        if (desc is { } d) ShowEnrollment(d.SecretBase32, d.OtpAuthUri);
        else { TempData.Remove(PendingKey); Enabled = twoFactor.IsEnabled(UserId); }
        return Page();
    }

    /// <summary>2FA'yı devre dışı bırak — yalnız geçerli bir kod ile (kullanıcının kendisi).</summary>
    public IActionResult OnPostDisable()
    {
        if (UserId == 0) return RedirectToPage("/Login");

        if (twoFactor.Disable(UserId, Code))
        {
            Enabled = false;
            StatusMessage = "İki adımlı doğrulama devre dışı bırakıldı.";
        }
        else
        {
            Enabled = twoFactor.IsEnabled(UserId);
            Error = "Kod doğrulanamadı; 2FA devre dışı bırakılmadı.";
        }
        return Page();
    }

    private void ShowEnrollment(string secretBase32, string otpAuthUri)
    {
        Enrolling = true;
        ManualKey = GroupKey(secretBase32);
        QrDataUri = BuildQrDataUri(otpAuthUri);
    }

    /// <summary>otpauth URI → inline PNG data URI (QRCoder, sunucu tarafı; harici CDN YOK).</summary>
    private static string BuildQrDataUri(string uri)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        byte[] png = new PngByteQRCode(data).GetGraphic(6);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    /// <summary>Elle giriş için secret'ı 4'erli gruplar (okunurluk).</summary>
    private static string GroupKey(string key)
        => string.Join(" ", System.Text.RegularExpressions.Regex.Matches(key, ".{1,4}").Select(m => m.Value));
}
