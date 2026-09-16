using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRestWeb.Services;

namespace MindAeonRestWeb.Pages;

/// <summary>
/// Login akışının 2FA (TOTP) adımı — parola doğru + kullanıcının 2FA'sı etkin olduğunda, cookie oturumu
/// açılmadan ÖNCE gelir. Pre-auth zarfı (kimlik master'da doğrulandı; henüz oturum YOK) TempData'da taşınır.
///
/// Doğrulama başarılıysa mevcut akışa devam: tek şube → doğrudan oturum + yönlendirme (Login ile aynı SSO),
/// çok şube → TwoFactorPending=false ile /SelectBranch. Enumerasyona kapalı (tek-tip hata); IP bazlı "login"
/// rate-limit + kullanıcı bazlı kod deneme kilidi (TwoFactorManager) ile kaba-kuvvete kapalı.
/// </summary>
[EnableRateLimiting("login")]
public class TwoFactorChallengeModel(TwoFactorManager twoFactor, IConfiguration config,
    MindAeonRestWeb.Auth.PwaTokenIssuer pwaToken) : PageModel
{
    [BindProperty] public string? Code { get; set; }
    public string? Error { get; private set; }

    /// <summary>Kullanıcının kime ait olduğunu gösteren kısa bağlam (enumerasyona açmadan; zaten parolayı bilen kişi).</summary>
    public string Who { get; private set; } = "";

    private const string GenericError = "Doğrulama kodu hatalı ya da süresi geçti.";

    private LoginModel.PreAuth? Read()
    {
        var json = TempData.Peek("preauth") as string;   // Peek: POST için de kalsın
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<LoginModel.PreAuth>(json);
    }

    public IActionResult OnGet()
    {
        var pre = Read();
        if (pre is null || !pre.TwoFactorPending) return RedirectToPage("/Login");
        Who = $"{pre.CompanyCode} · {pre.FullName}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var pre = Read();
        if (pre is null || !pre.TwoFactorPending) return RedirectToPage("/Login");
        Who = $"{pre.CompanyCode} · {pre.FullName}";

        // Kod doğrulama (kullanıcı bazlı deneme kilitli). Hata tek-tip → enumerasyon/oracle yok.
        if (!twoFactor.VerifyLogin(pre.UserId, Code))
        {
            Error = GenericError;
            return Page();
        }

        // Kimlik + 2FA doğrulandı. Tek şube → doğrudan oturum aç (Login ile aynı SSO devri).
        if (pre.Branches.Count == 1)
        {
            TempData.Remove("preauth");
            var (user, company, lic) = Rebuild(pre);
            return await WebSignIn.CompleteAsync(this, config, pwaToken,
                user, company, pre.Branches[0].Id, pre.Roles, lic);
        }

        // Çok şube → bayrağı düşür (challenge geçildi) ve mevcut şube-seçimi akışına devam et.
        pre.TwoFactorPending = false;
        TempData["preauth"] = JsonSerializer.Serialize(pre);
        return RedirectToPage("/SelectBranch");
    }

    private static (User User, Company Company, LicenseStatus Lic) Rebuild(LoginModel.PreAuth pre)
    {
        var user = new User { Id = pre.UserId, Username = pre.Username, FullName = pre.FullName };
        var company = new Company { Id = pre.CompanyId, Code = pre.CompanyCode, Name = pre.CompanyCode, DatabaseName = pre.DatabaseName };
        return (user, company, WebLoginService.ReloadLicense(pre));
    }
}
