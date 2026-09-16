using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRestWeb.Auth;

namespace MindAeonRestWeb.Services;

/// <summary>Kimlik + (varsa 2FA) doğrulandıktan sonraki SON adım: cookie oturumu aç + role göre yönlendir.
/// Login (tek şube) ve TwoFactorChallenge (tek şube) tarafından PAYLAŞILIR → PWA SSO devri tek yerde.</summary>
public static class WebSignIn
{
    /// <summary>Principal kur → cookie SignIn → back-office'e ya da (POS rolleri) PWA'ya SSO token'ıyla yönlendir.</summary>
    public static async Task<IActionResult> CompleteAsync(
        PageModel page, IConfiguration config, PwaTokenIssuer pwaToken,
        User user, Company company, int branchId, IReadOnlyList<string> roles, LicenseStatus lic)
    {
        var principal = WebLoginService.BuildPrincipal(user, company, branchId, roles, lic);
        await page.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        if (WebLoginService.Landing(roles) == "backoffice")
            return page.RedirectToPage("/BackOffice/Index");

        // POS rolleri → merkezî PWA. Çift login'i önlemek için aynı imzalı JWT'yi URL fragment'ıyla devret (SSO).
        var pwa = (config["ApiBaseUrl"] ?? "").TrimEnd('/') + "/app";   // boş → aynı origin (/app)
        int kioskNo = int.TryParse(config["PosKioskNo"], out var k) && k > 0 ? k : 1;
        try
        {
            var token = pwaToken.Create(user.Id, user.FullName ?? user.Username, company.Code, company.DatabaseName,
                branchId, roles, kioskNo, (long)lic.Features, lic.Package.ToString(),
                lic.MaxUsers, lic.MaxBranches, lic.MaxKiosks, lic.ExpiresUtc);
            // Fragment (#) → token sunucuya/loglara gitmez; PWA boot'ta okunup localStorage'a alınır.
            return new RedirectResult($"{pwa}#token={Uri.EscapeDataString(token)}");
        }
        catch { return new RedirectResult(pwa); }   // anahtar çözülemezse eski davranış (PWA kendi girişini yapar)
    }
}
