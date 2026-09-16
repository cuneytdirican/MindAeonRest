using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRest.Licence.Portal.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        // Role göre landing'e yönlendir (varsayılan [Authorize] zaten girişi zorunlu kılar).
        if (User.IsInRole("Admin")) return RedirectToPage("/Admin/Index");
        if (User.IsInRole("Dealer")) return RedirectToPage("/Dealer/Index");
        if (User.IsInRole("Customer")) return RedirectToPage("/Musteri/Index");
        return RedirectToPage("/Login");
    }
}
