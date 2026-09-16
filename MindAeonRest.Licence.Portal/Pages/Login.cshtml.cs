using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages;

[AllowAnonymous]
public class LoginModel(ApiClient api) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // Zaten girişliyse rolüne göre yönlendir
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToLanding();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "E-posta ve parola gereklidir.";
            return Page();
        }

        var result = await api.LoginAsync(Email.Trim(), Password);
        if (result is null || string.IsNullOrEmpty(result.Token))
        {
            ErrorMessage = "E-posta veya parola hatalı.";
            return Page();
        }

        // JWT'den ek claim'leri (dealer/business/sub) çıkar; JWT'nin kendisini cookie'de sakla.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, string.IsNullOrEmpty(result.FullName) ? Email : result.FullName),
            new(ApiClient.JwtClaimType, result.Token),
            new("name", string.IsNullOrEmpty(result.FullName) ? Email : result.FullName),
            new("kind", result.Kind ?? "")
        };
        if (!string.IsNullOrEmpty(result.Role))
            claims.Add(new Claim(ClaimTypes.Role, result.Role));

        foreach (var c in jwt.Claims)
        {
            if (c.Type is "dealer" or "business" or "sub")
                claims.Add(new Claim(c.Type, c.Value));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = result.ExpiresUtc
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), props);

        return RedirectToLanding(result.Role);
    }

    private IActionResult RedirectToLanding(string? role = null)
    {
        role ??= User.IsInRole("Admin") ? "Admin"
               : User.IsInRole("Dealer") ? "Dealer"
               : User.IsInRole("Customer") ? "Customer" : "";
        return role switch
        {
            "Admin" => RedirectToPage("/Admin/Index"),
            "Dealer" => RedirectToPage("/Dealer/Index"),
            "Customer" => RedirectToPage("/Musteri/Index"),
            _ => RedirectToPage("/Index")
        };
    }
}
