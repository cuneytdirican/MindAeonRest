using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

/// <summary>
/// Yeni lisanslı POS tenant'ı oluşturur. Form → OnPost → PosProvisionClient.CreateTenantAsync.
/// Dönen geçici admin parolası BİR KEZ ekranda gösterilir; hiçbir yerde saklanmaz (kalıcılık yok).
/// X-Provision-Key sunucuda kalır (client içinde eklenir). [Authorize(Roles=Admin)].
/// Opsiyonel "Lisans Paketi" seçilirse tenant oluşturulduktan SONRA otomatik lisans atanır + POS'a kurulur
/// (<see cref="TenantLicenseAssigner"/>). Lisans adımının hatası tenant sonucunu/geçici parolayı GİZLEMEZ;
/// sonuç panelinde ayrıca gösterilir (Tenant'lar ekranından "Lisans Ata" ile tekrar denenebilir).
/// </summary>
[Authorize(Roles = "Admin")]
public class NewTenantModel(PosProvisionClient pos, IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string AdminEmail { get; set; } = "";
    [BindProperty] public string AdminFullName { get; set; } = "";

    /// <summary>Opsiyonel lisans paketi (LicensePackage int). Null → lisans atanmaz (tenant Lite denemede başlar).</summary>
    [BindProperty] public int? LicensePackage { get; set; }
    [BindProperty] public DurationKind LicenseDuration { get; set; } = DurationKind.Perpetual;
    [BindProperty] public DateTime? LicenseExpires { get; set; }

    /// <summary>Lisans atama sonucu (paket seçildiyse). Null → lisans adımı istenmedi.</summary>
    public TenantLicenseAssignOutcome? LicenseOutcome { get; private set; }

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

        // Lisans girdisi tenant oluşturulmadan ÖNCE doğrulanır (tenant açılıp lisans formu yüzünden yarım kalmasın).
        if (LicensePackage is { } lp)
        {
            if (!Enum.IsDefined(typeof(MindAeonRest.Licensing.LicensePackage), lp))
            {
                Error = "Geçersiz lisans paketi.";
                return Page();
            }
            if (LicenseDuration != DurationKind.Perpetual && (LicenseExpires is not { } le || le.Date < DateTime.UtcNow.Date))
            {
                Error = "Süreli (Aylık/Yıllık) lisans için bugünden sonraki bir bitiş tarihi seçin.";
                return Page();
            }
        }

        var (value, error) = await pos.CreateTenantAsync(
            new CreateTenantRequest(Code, Name, AdminEmail, AdminFullName), ct);

        if (value is null)
        {
            Error = error ?? "Tenant oluşturulamadı.";
            return Page();
        }

        Result = value;

        // Opsiyonel: lisans ata + POS'a kur. Hata olsa bile Result (geçici parola) gösterilmeye devam eder.
        if (LicensePackage is { } pkg)
        {
            try
            {
                LicenseOutcome = await TenantLicenseAssigner.AssignAsync(_api, pos, value.Code, value.Name,
                    new TenantLicenseForm(pkg, LicenseDuration, LicenseExpires, 0, 0, 0, 0, "Yeni tenant oluşturmada atandı."), ct);
            }
            catch (Exception ex)
            {
                LicenseOutcome = new TenantLicenseAssignOutcome(false, $"Lisans atanamadı: {ex.Message}", null, null, false);
            }
        }
        return Page();
    }
}
