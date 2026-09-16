using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DealersModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<DealerListItem> Items { get; private set; } = [];
    public DealerDetail? Detail { get; private set; }
    public DealerEarnings? Earnings { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<DealerListItem>>("/api/v1/dealers?includeInactive=true", ct) ?? [];
    }

    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<DealerListItem>>("/api/v1/dealers?includeInactive=true", ct) ?? [];
        return Partial("_DealersTable", this);
    }

    public async Task<IActionResult> OnGetDetailAsync(int id, CancellationToken ct)
    {
        Detail = await _api.GetAsync<DealerDetail>($"/api/v1/dealers/{id}", ct);
        Earnings = await _api.GetAsync<DealerEarnings>($"/api/v1/dealers/{id}/earnings", ct);
        return Partial("_DealerDetail", this);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        string name, string? code, string? contactName, string? email, string? phone,
        decimal marginPercent, BillingMode billingMode, string? notes, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync("/api/v1/dealers",
            new { name, code, contactName, email, phone, marginPercent, billingMode, notes }, ct);
        return await RefreshAsync(ok, err, "Bayi oluşturuldu.", ct);
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        int id, string name, string? contactName, string? email, string? phone,
        decimal marginPercent, BillingMode billingMode, bool isActive, string? notes, CancellationToken ct)
    {
        var (ok, err) = await _api.PutAsync($"/api/v1/dealers/{id}",
            new { name, contactName, email, phone, marginPercent, billingMode, isActive, notes }, ct);
        return await RefreshAsync(ok, err, "Bayi güncellendi.", ct);
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
        Items = await _api.GetAsync<List<DealerListItem>>("/api/v1/dealers?includeInactive=true", ct) ?? [];
        return Partial("_DealersTable", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
