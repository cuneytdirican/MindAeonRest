using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRestWeb.Services;

namespace MindAeonRestWeb.Pages;

public class SelectBranchModel(WebLoginService login, IConfiguration config) : PageModel
{
    public LoginModel.PreAuth? Pre { get; private set; }
    [BindProperty] public int BranchId { get; set; }
    public string? Error { get; private set; }

    private LoginModel.PreAuth? Read()
    {
        var json = TempData.Peek("preauth") as string;   // Peek: POST için de kalsın
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<LoginModel.PreAuth>(json);
    }

    public IActionResult OnGet()
    {
        Pre = Read();
        if (Pre is null) return RedirectToPage("/Login");
        // 2FA henüz doğrulanmadıysa şube seçimi (ve dolayısıyla oturum) ATLANAMAZ → challenge'a geri gönder.
        if (Pre.TwoFactorPending) return RedirectToPage("/TwoFactorChallenge");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Pre = Read();
        if (Pre is null) return RedirectToPage("/Login");
        // 2FA bekliyor: challenge'ı atlayıp doğrudan POST ile oturum açılamaz (bypass koruması).
        if (Pre.TwoFactorPending) return RedirectToPage("/TwoFactorChallenge");

        // Seçilen şube pre-auth'taki izinli listede mi + erişim doğrulaması (güvenlik)
        if (!Pre.Branches.Any(b => b.Id == BranchId) ||
            !login.VerifyBranch(Pre.UserId, Pre.CompanyId, BranchId, Pre.IsSysAdmin))
        {
            Error = "Geçersiz şube seçimi.";
            return Page();
        }

        TempData.Remove("preauth");

        var user = new User { Id = Pre.UserId, Username = Pre.Username, FullName = Pre.FullName };
        var company = new Company { Id = Pre.CompanyId, Code = Pre.CompanyCode, Name = Pre.CompanyCode, DatabaseName = Pre.DatabaseName };
        Enum.TryParse<LicensePackage>(Pre.Package, out var pkg);
        var lic = new LicenseStatus { State = LicenseState.Licensed, Package = pkg, Features = (Feature)Pre.Features };

        var principal = WebLoginService.BuildPrincipal(user, company, BranchId, Pre.Roles, lic);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        if (WebLoginService.Landing(Pre.Roles) == "backoffice")
            return RedirectToPage("/BackOffice/Index");
        var pwa = (config["ApiBaseUrl"] ?? "http://localhost:5000").TrimEnd('/') + "/app";
        return Redirect(pwa);
    }
}
