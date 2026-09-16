using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

/// <summary>
/// Yeni lisanslı POS tenant'ı oluşturur. Form → OnPost → PosProvisionClient.CreateTenantAsync.
/// Dönen geçici admin parolası BİR KEZ ekranda gösterilir; hiçbir yerde saklanmaz (kalıcılık yok).
/// X-Provision-Key sunucuda kalır (client içinde eklenir). [Authorize(Roles=Admin)].
/// </summary>
[Authorize(Roles = "Admin")]
public class NewTenantModel(PosProvisionClient pos) : PageModel
{
    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string AdminEmail { get; set; } = "";
    [BindProperty] public string AdminFullName { get; set; } = "";

    /// <summary>Başarılı oluşturmada dolar; dolu ise form yerine sonuç paneli (parola) gösterilir.</summary>
    public CreateTenantResult? Result { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Anahtar yoksa formu devre dışı bırakıp uyarı göstermek için.</summary>
    public bool ProvisioningEnabled => pos.IsConfigured;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Code = (Code ?? "").Trim();
        Name = (Name ?? "").Trim();
        AdminEmail = (AdminEmail ?? "").Trim();
        AdminFullName = (AdminFullName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name) ||
            string.IsNullOrWhiteSpace(AdminEmail) || string.IsNullOrWhiteSpace(AdminFullName))
        {
            Error = "Tüm alanlar zorunludur.";
            return Page();
        }

        var (value, error) = await pos.CreateTenantAsync(
            new CreateTenantRequest(Code, Name, AdminEmail, AdminFullName), ct);

        if (value is null)
        {
            Error = error ?? "Tenant oluşturulamadı.";
            return Page();
        }

        Result = value;
        return Page();
    }
}
