using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRestWeb.Pages;

public class LogoutModel : PageModel
{
    /// <summary>Cookie'yi düşürür VE (aynı origin olduğundan) PWA'nın localStorage JWT/oturumunu da temizler
    /// → back-office çıkışı POS oturumunu da sonlandırır (paylaşılan terminalde başka kullanıcının adına iş yapılmaz).
    /// Bu yüzden Redirect yerine küçük bir JS sayfası döndürülür; JS mag.* anahtarlarını silip /Index'e yönlendirir.</summary>
    public async Task<IActionResult> OnGet()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Page();
    }
}
