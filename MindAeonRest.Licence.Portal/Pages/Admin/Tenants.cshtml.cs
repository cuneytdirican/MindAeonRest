using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

/// <summary>
/// POS tenant'ları için gözetim (oversight) ekranı. POS provisioning API'sini SUNUCU-TARAFI çağırır;
/// X-Provision-Key tarayıcıya gitmez. Listeler + satır detayı (kullanıcılar/şubeler) + "Lisans Ata"
/// (Licence API'de merkezî lisans üret → POS'a kur; bkz. <see cref="TenantLicenseAssigner"/>).
/// </summary>
[Authorize(Roles = "Admin")]
public class TenantsModel(PosProvisionClient pos, IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<TenantSummary> Tenants { get; private set; } = [];
    public IReadOnlyList<AddOnCatalogItem> AddOns { get; private set; } = [];
    public string? LoadError { get; private set; }

    // Detay partial için
    public string? DetailCode { get; private set; }
    public string? DetailName { get; private set; }
    public IReadOnlyList<TenantUser> Users { get; private set; } = [];
    public IReadOnlyList<TenantBranch> Branches { get; private set; } = [];
    public string? DetailError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    /// <summary>Satırdan açılan detay: seçilen tenant'ın kullanıcı + şube listeleri (htmx partial).</summary>
    public async Task<IActionResult> OnGetDetailAsync(string code, string? name, CancellationToken ct)
    {
        DetailCode = code;
        DetailName = name;

        var (users, uErr) = await pos.ListTenantUsersAsync(code, ct);
        var (branches, bErr) = await pos.ListTenantBranchesAsync(code, ct);
        Users = users ?? [];
        Branches = branches ?? [];
        DetailError = uErr ?? bErr;

        return Partial("_TenantDetail", this);
    }

    /// <summary>htmx: "Lisans Ata" modalı → Licence API'de üret + POS'a kur → tabloyu yenile + toast.</summary>
    public async Task<IActionResult> OnPostAssignLicenseAsync(string code, string? name, int package, DurationKind duration,
        DateTime? expiresUtc, long[]? addOns, int extraUsers, int extraKiosks, int extraBranches, string? note, CancellationToken ct)
    {
        long addOnFeatures = 0;
        if (addOns is not null) foreach (var b in addOns) addOnFeatures |= b;

        var outcome = await TenantLicenseAssigner.AssignAsync(_api, pos, code, name,
            new TenantLicenseForm(package, duration, expiresUtc, addOnFeatures, extraUsers, extraKiosks, extraBranches, note), ct);

        if (!outcome.Ok)
        {
            Response.StatusCode = 400;
            Response.Headers["HX-Trigger"] = TriggerJson(outcome.Message, "danger");
            return Content("");
        }

        Response.Headers["HX-Trigger"] = TriggerJson(outcome.Message, "success");
        await LoadAsync(ct);
        return Partial("_TenantsTable", this);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var (value, error) = await pos.ListTenantsAsync(ct);
        Tenants = value ?? [];
        LoadError = error;
        AddOns = await _api.GetAsync<List<AddOnCatalogItem>>("/api/v1/catalog/addons", ct) ?? [];
    }

    private static string TriggerJson(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["portalToast"] = new { message = msg, kind }
        });
}
