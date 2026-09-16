using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRestWeb.Services;

namespace MindAeonRestWeb.Pages;

[EnableRateLimiting("login")]
public class LoginModel(WebLoginService login, IConfiguration config, MindAeonRestWeb.Auth.PwaTokenIssuer pwaToken) : PageModel
{
    [BindProperty] public string? CompanyCode { get; set; }
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }

    public string? Error { get; private set; }
    /// <summary>Şirket kodu + kullanıcı adı hatırlandıysa true → görünüm parolaya odaklanır.</summary>
    public bool Prefilled { get; private set; }

    // Son BAŞARILI girişin şirket kodu + kullanıcı adı (parola ASLA) bu tarayıcıda hatırlanır (yalnız kolaylık).
    private const string LastLoginCookie = "MindAeon.LastLogin";

    public void OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            Response.Redirect("/BackOffice");
            return;
        }
        // Son giriş yapılan şirket + kullanıcıyı çerezden doldur (kullanıcı isterse değiştirir; parola boş kalır).
        if (Request.Cookies.TryGetValue(LastLoginCookie, out var raw) && !string.IsNullOrEmpty(raw))
        {
            var parts = raw.Split('|');
            if (parts.Length == 2)
            {
                CompanyCode = Uri.UnescapeDataString(parts[0]);
                Username = Uri.UnescapeDataString(parts[1]);
                Prefilled = !string.IsNullOrWhiteSpace(CompanyCode) && !string.IsNullOrWhiteSpace(Username);
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var r = login.Authenticate(CompanyCode, Username, Password);
        if (!r.Ok)
        {
            Error = r.Error;
            return Page();
        }

        // Başarılı giriş → normalize şirket kodu + kullanıcı adını hatırla (parola HARİÇ). 180 gün, HttpOnly.
        RememberLogin(r.Company!.Code, r.User!.Username);

        // 2FA etkinse: cookie SignIn'den ÖNCE TOTP challenge iste. Pre-auth (kimlik master'da doğrulandı,
        // henüz oturum YOK) TempData'da (DataProtection korumalı) taşınır; TwoFactorPending=true → şube seçimi
        // challenge'ı ATLAYAMAZ (SelectBranch bu bayrağı denetler). Doğrulama sonrası mevcut akışa devam edilir.
        if (r.TwoFactorEnabled)
        {
            TempData["preauth"] = JsonSerializer.Serialize(BuildPreAuth(r, twoFactorPending: true));
            return RedirectToPage("/TwoFactorChallenge");
        }

        // Tek şube → doğrudan giriş. Çok şube → şube seçimine taşı (pre-auth TempData'da, DataProtection korumalı).
        if (r.Branches.Count == 1)
            return await SignInAndRedirect(r.User!, r.Company!, r.Branches[0].Id, r.Roles, r.License!);

        TempData["preauth"] = JsonSerializer.Serialize(BuildPreAuth(r, twoFactorPending: false));
        return RedirectToPage("/SelectBranch");
    }

    /// <summary>Son başarılı girişin şirket kodu + kullanıcı adını bu tarayıcıda hatırlar (parola ASLA saklanmaz).</summary>
    private void RememberLogin(string companyCode, string username)
    {
        var val = Uri.EscapeDataString(companyCode) + "|" + Uri.EscapeDataString(username);
        Response.Cookies.Append(LastLoginCookie, val, new CookieOptions
        {
            HttpOnly = true,                       // yalnız sunucu okur (OnGet) — XSS ile sızmaz
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,              // üretimde HTTPS → yalnız güvenli kanal
            Expires = DateTimeOffset.UtcNow.AddDays(180),
            IsEssential = true                     // çerez onayından muaf (işlevsel)
        });
    }

    /// <summary>Aşamalar (şube seçimi / 2FA challenge) arası taşınan pre-auth zarfını kurar.</summary>
    private static PreAuth BuildPreAuth(WebLoginService.StageResult r, bool twoFactorPending) => new()
    {
        UserId = r.User!.Id, Username = r.User.Username, FullName = r.User.FullName ?? r.User.Username,
        CompanyId = r.Company!.Id, CompanyCode = r.Company.Code, DatabaseName = r.Company.DatabaseName,
        IsSysAdmin = r.IsSysAdmin, Features = (long)r.License!.Features, Package = r.License.Package.ToString(),
        Roles = r.Roles, Branches = r.Branches.Select(b => new PreAuth.B { Id = b.Id, Name = b.Name }).ToList(),
        TwoFactorPending = twoFactorPending
    };

    private Task<IActionResult> SignInAndRedirect(User user, Company company, int branchId,
        IReadOnlyList<string> roles, LicenseStatus lic)
        => WebSignIn.CompleteAsync(this, config, pwaToken, user, company, branchId, roles, lic);

    // Şube seçimi arası taşınan pre-auth (identity master'da doğrulandı; SelectBranch erişimi yeniden denetler).
    public sealed class PreAuth
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public int CompanyId { get; set; }
        public string CompanyCode { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public bool IsSysAdmin { get; set; }
        public long Features { get; set; }
        public string Package { get; set; } = "Lite";
        public List<string> Roles { get; set; } = new();
        public List<B> Branches { get; set; } = new();
        /// <summary>2FA doğrulaması BEKLİYOR mu — true iken SelectBranch oturum AÇMAZ (challenge atlanamaz).</summary>
        public bool TwoFactorPending { get; set; }
        public sealed class B { public int Id { get; set; } public string Name { get; set; } = ""; }
    }
}
