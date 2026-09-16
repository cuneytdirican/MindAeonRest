using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class IndexModel(ApiClient api) : PageModel
{
    public List<BusinessListItemVm> Businesses { get; private set; } = [];
    public List<RequestListItemVm> Requests { get; private set; } = [];
    public List<DemoRespVm> Demos { get; private set; } = [];
    public List<PaymentRespVm> Payments { get; private set; } = [];

    // Marj kazanç özeti — bayinin ödemelerinden hesaplanır (earnings ucu Admin'e özel).
    public decimal MarginPaid { get; private set; }
    public decimal MarginPending { get; private set; }
    public decimal GrossPaid { get; private set; }
    public string Currency { get; private set; } = "TRY";

    public int BusinessCount => Businesses.Count;
    public int ActiveLicenseTotal => Businesses.Sum(b => b.ActiveLicenseCount);
    public int PendingRequests => Requests.Count(r => r.Status == 0);
    public int OpenDemos => Demos.Count(d => !d.Converted);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Businesses = await api.GetAsync<List<BusinessListItemVm>>("/api/v1/businesses", ct) ?? [];
        Requests = await api.GetAsync<List<RequestListItemVm>>("/api/v1/requests/mine", ct) ?? [];
        Demos = await api.GetAsync<List<DemoRespVm>>("/api/v1/demos", ct) ?? [];
        Payments = await api.GetAsync<List<PaymentRespVm>>("/api/v1/payments", ct) ?? [];

        var paid = Payments.Where(p => p.Status == 1).ToList();   // Paid
        var pending = Payments.Where(p => p.Status == 0).ToList(); // Pending
        MarginPaid = paid.Sum(p => p.MarginAmount);
        MarginPending = pending.Sum(p => p.MarginAmount);
        GrossPaid = paid.Sum(p => p.Amount);
        Currency = Payments.FirstOrDefault()?.CurrencyCode ?? "TRY";
    }
}
