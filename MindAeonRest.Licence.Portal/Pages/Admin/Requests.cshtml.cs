using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class RequestsModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<RequestListItem> Items { get; private set; } = [];
    public IReadOnlyList<AddOnCatalogItem> AddOns { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<RequestListItem>>($"/api/v1/requests?status={(int)Status}", ct) ?? [];
        AddOns = await _api.GetAsync<List<AddOnCatalogItem>>("/api/v1/catalog/addons", ct) ?? [];
    }

    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<RequestListItem>>($"/api/v1/requests?status={(int)Status}", ct) ?? [];
        return Partial("_RequestsTable", this);
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, DurationKind duration, DateTime? expiresUtc, string? notes,
        long[]? addOns, int extraUsers, int extraKiosks, int extraBranches, CancellationToken ct)
    {
        long addOnFeatures = 0;
        if (addOns is not null) foreach (var b in addOns) addOnFeatures |= b;
        expiresUtc = ExpiryNormalizer.ToEndOfDayUtc(expiresUtc);
        var (ok, err) = await _api.PostAsync($"/api/v1/requests/{id}/approve",
            new { duration, expiresUtc, notes, addOnFeatures, extraUsers, extraKiosks, extraBranches }, ct);
        return await RefreshAsync(ok, err, "Talep onaylandı, lisans üretildi.", ct);
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string? reason, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync($"/api/v1/requests/{id}/reject", new { reason }, ct);
        return await RefreshAsync(ok, err, "Talep reddedildi.", ct);
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
        Items = await _api.GetAsync<List<RequestListItem>>($"/api/v1/requests?status={(int)Status}", ct) ?? [];
        return Partial("_RequestsTable", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
