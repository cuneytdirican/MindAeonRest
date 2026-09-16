using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

[Authorize(Roles = "Customer")]
public class HesabimModel : PageModel
{
    public string FullName => User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Kullanıcı";
    public string? Email => User.Identity?.Name;
    public string? BusinessId => User.FindFirst("business")?.Value;
    public string Role => "Müşteri";

    public void OnGet() { }
}
