using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class PackagesModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<PackagePriceItem> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<PackagePriceItem>>("/api/v1/package-prices?includeInactive=true", ct) ?? [];
    }

    public async Task<IActionResult> OnPostCreateAsync(int package, DurationKind duration, decimal basePrice, string? currencyCode, bool isActive, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync("/api/v1/package-prices",
            new { package, duration, basePrice, currencyCode, isActive }, ct);
        return await RefreshAsync(ok, err, "Fiyat satırı eklendi.", ct);
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, int package, DurationKind duration, decimal basePrice, string? currencyCode, bool isActive, CancellationToken ct)
    {
        var (ok, err) = await _api.PutAsync($"/api/v1/package-prices/{id}",
            new { package, duration, basePrice, currencyCode, isActive }, ct);
        return await RefreshAsync(ok, err, "Fiyat güncellendi.", ct);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        var (ok, err) = await _api.DeleteAsync($"/api/v1/package-prices/{id}", ct);
        return await RefreshAsync(ok, err, "Fiyat satırı silindi.", ct);
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
        Items = await _api.GetAsync<List<PackagePriceItem>>("/api/v1/package-prices?includeInactive=true", ct) ?? [];
        return Partial("_PackagesTable", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
