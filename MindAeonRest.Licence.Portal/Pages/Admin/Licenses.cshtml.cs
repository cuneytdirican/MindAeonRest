using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class LicensesModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<LicenseListItem> Items { get; private set; } = [];
    public IReadOnlyList<AddOnCatalogItem> AddOns { get; private set; } = [];
    public LicenseDetail? Detail { get; private set; }

    [BindProperty(SupportsGet = true)] public int? Package { get; set; }
    [BindProperty(SupportsGet = true)] public LicenseStatus? Status { get; set; }

    private string BuildQuery()
    {
        var qs = new List<string>();
        if (Package is { } p) qs.Add($"package={p}");
        if (Status is { } s) qs.Add($"status={(int)s}");
        return qs.Count == 0 ? "" : "?" + string.Join("&", qs);
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<LicenseListItem>>("/api/v1/admin/licenses" + BuildQuery(), ct) ?? [];
        AddOns = await _api.GetAsync<List<AddOnCatalogItem>>("/api/v1/catalog/addons", ct) ?? [];
    }

    // htmx: filtre uygulanmış tablo gövdesi (kısmi render)
    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<LicenseListItem>>("/api/v1/admin/licenses" + BuildQuery(), ct) ?? [];
        AddOns = await _api.GetAsync<List<AddOnCatalogItem>>("/api/v1/catalog/addons", ct) ?? [];
        return Partial("_LicensesTable", this);
    }

    // htmx: tek lisans detay paneli (timeline dahil)
    public async Task<IActionResult> OnGetDetailAsync(int id, CancellationToken ct)
    {
        Detail = await _api.GetAsync<LicenseDetail>($"/api/v1/admin/licenses/{id}", ct);
        return Partial("_LicenseDetail", this);
    }

    public async Task<IActionResult> OnPostUpgradeAsync(int id, int package, DateTime? expiresUtc, string? note,
        long[]? addOns, int extraUsers, int extraKiosks, int extraBranches, CancellationToken ct)
    {
        long addOnFeatures = 0;
        if (addOns is not null) foreach (var b in addOns) addOnFeatures |= b;
        expiresUtc = ExpiryNormalizer.ToEndOfDayUtc(expiresUtc);
        var (ok, err) = await _api.PostAsync($"/api/v1/admin/licenses/{id}/upgrade",
            new { package, expiresUtc, note, addOnFeatures, extraUsers, extraKiosks, extraBranches }, ct);
        return await RefreshOrErrorAsync(ok, err, "Lisans yükseltildi.", ct);
    }

    public async Task<IActionResult> OnPostRenewAsync(int id, DateTime? expiresUtc, string? note, CancellationToken ct)
    {
        expiresUtc = ExpiryNormalizer.ToEndOfDayUtc(expiresUtc);
        var (ok, err) = await _api.PostAsync($"/api/v1/admin/licenses/{id}/renew",
            new { expiresUtc, note }, ct);
        return await RefreshOrErrorAsync(ok, err, "Lisans yenilendi.", ct);
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id, string? note, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync($"/api/v1/admin/licenses/{id}/revoke",
            new { note }, ct);
        return await RefreshOrErrorAsync(ok, err, "Lisans iptal edildi.", ct);
    }

    private async Task<IActionResult> RefreshOrErrorAsync(bool ok, string? err, string okMsg, CancellationToken ct)
    {
        if (!ok)
        {
            Response.StatusCode = 400;
            Response.Headers["HX-Trigger"] = TriggerJson(err ?? "İşlem başarısız.", "danger");
            return Content("");
        }
        Response.Headers["HX-Trigger"] = TriggerJson(okMsg, "success");
        Items = await _api.GetAsync<List<LicenseListItem>>("/api/v1/admin/licenses" + BuildQuery(), ct) ?? [];
        AddOns = await _api.GetAsync<List<AddOnCatalogItem>>("/api/v1/catalog/addons", ct) ?? [];
        return Partial("_LicensesTable", this);
    }

    private static string TriggerJson(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["portalToast"] = new { message = msg, kind }
        });
}
