using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class PaymentsModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<PaymentItem> Items { get; private set; } = [];

    public decimal TotalGross => Items.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
    public decimal TotalMargin => Items.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.MarginAmount);
    public decimal TotalNet => Items.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.NetAmount);
    public decimal Pending => Items.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.Amount);
    public string Currency => Items.Where(p => p.Status == PaymentStatus.Paid)
        .GroupBy(p => p.CurrencyCode).OrderByDescending(g => g.Count())
        .Select(g => g.Key).FirstOrDefault() ?? "TRY";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<PaymentItem>>("/api/v1/payments", ct) ?? [];
    }

    public async Task<IActionResult> OnPostApproveTransferAsync(int id, string? providerRef, string? note, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync($"/api/v1/payments/{id}/approve-bank-transfer",
            new { providerRef, note }, ct);
        if (!ok)
        {
            Response.StatusCode = 400;
            Response.Headers["HX-Trigger"] = Toast(err ?? "İşlem başarısız.", "danger");
            return Content("");
        }
        Response.Headers["HX-Trigger"] = Toast("Havale onaylandı.", "success");
        Items = await _api.GetAsync<List<PaymentItem>>("/api/v1/payments", ct) ?? [];
        return Partial("_PaymentsTable", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
