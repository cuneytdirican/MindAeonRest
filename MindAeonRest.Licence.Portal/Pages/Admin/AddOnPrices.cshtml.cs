using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class AddOnPricesModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<AddOnPriceItem> Items { get; private set; } = [];
    public IReadOnlyList<AddOnCatalogItem> Modules { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(int kind, long featureBit, string name, decimal unitPrice, string? currencyCode, bool isActive, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync("/api/v1/addon-prices",
            new { kind, featureBit, name, unitPrice, currencyCode, isActive }, ct);
        return await RefreshAsync(ok, err, "Add-on fiyatı eklendi.", ct);
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, int kind, long featureBit, string name, decimal unitPrice, string? currencyCode, bool isActive, CancellationToken ct)
    {
        var (ok, err) = await _api.PutAsync($"/api/v1/addon-prices/{id}",
            new { kind, featureBit, name, unitPrice, currencyCode, isActive }, ct);
        return await RefreshAsync(ok, err, "Add-on fiyatı güncellendi.", ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<AddOnPriceItem>>("/api/v1/addon-prices?includeInactive=true", ct) ?? [];
        Modules = await _api.GetAsync<List<AddOnCatalogItem>>("/api/v1/catalog/addons", ct) ?? [];
    }

    private async Task<IActionResult> RefreshAsync(bool ok, string? err, string okMsg, CancellationToken ct)
    {
        if (!ok)
        {
            Response.StatusCode = 400;
            Response.Headers["HX-Trigger"] = Toast(err ?? "İşlem başarısız.", "danger");
            return Content("");
        }
        Response.Headers["HX-Trigger"] = Toast(okMsg, "success");
        await LoadAsync(ct);
        return Partial("_AddOnPricesTable", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
