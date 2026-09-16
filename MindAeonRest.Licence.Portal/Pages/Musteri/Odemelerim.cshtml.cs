using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

[Authorize(Roles = "Customer")]
public class OdemelerimModel(ApiClient api) : PageModel
{
    public List<PaymentVm> Payments { get; private set; } = new();
    public List<CatalogVm> Catalog { get; private set; } = new();

    public decimal TotalPaid => Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
    public decimal Pending => Payments.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.Amount);

    /// <summary>
    /// Müşterinin kendi ödeyip ödemediği. Mevcut bir kayıttan okunur; kayıt yoksa
    /// "müşteri öder" varsayımıyla ödeme aracı gösterilir (gerçek tahsilat sunucuda doğrulanır).
    /// </summary>
    public bool CustomerPays =>
        !Payments.Any() || Payments.Any(p => p.BillingMode == BillingMode.CustomerPaysUs);

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Payments = (await api.GetAsync<List<PaymentVm>>("/api/v1/payments", ct) ?? new())
            .OrderByDescending(p => p.CreatedUtc).ToList();
        Catalog = (await api.GetAsync<List<CatalogVm>>("/api/v1/package-prices/catalog", ct) ?? new())
            .OrderBy(c => c.Package).ThenBy(c => c.Duration).ToList();
    }

    /// <summary>htmx: ödeme başlat. Tutar SUNUCUDA hesaplanır (istemci tutar göndermez).</summary>
    public async Task<IActionResult> OnPostPayAsync(int package, int duration, int provider, CancellationToken ct)
    {
        var (resp, error) = await api.PostResultAsync<StartPaymentResultVm>("/api/v1/payments/start", new
        {
            provider,
            package,
            duration,
            businessId = (int?)null, // sunucu müşteri için kendi BusinessId'sini kullanır
            licenseId = (int?)null,
            returnUrl = $"{Request.Scheme}://{Request.Host}/Musteri/Odemelerim"
        }, ct);

        if (resp is null) ViewData["PayError"] = error;
        return Partial("_PayResult", resp);
    }
}
