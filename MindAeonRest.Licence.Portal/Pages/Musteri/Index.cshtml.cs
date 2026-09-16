using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

[Authorize(Roles = "Customer")]
public class IndexModel(ApiClient api) : PageModel
{
    public CustomerLicenseVm? License { get; private set; }
    public UsageVm? LatestUsage { get; private set; }
    public List<PaymentVm> Payments { get; private set; } = new();
    public List<TicketVm> Tickets { get; private set; } = new();

    public int? BusinessId =>
        int.TryParse(User.FindFirst("business")?.Value, out var b) ? b : null;

    public decimal TotalPaid => Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
    public int PendingPayments => Payments.Count(p => p.Status == PaymentStatus.Pending);
    public int OpenTickets => Tickets.Count(t => t.Status != TicketStatus.Closed);

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Lisans (varsa) — Admin uçu müşteriye 403 dönebilir; o durumda null kalır (zarif boş durum).
        var bizId = BusinessId;
        if (bizId is { } bid)
        {
            var lics = await api.GetAsync<List<CustomerLicenseVm>>("/api/v1/my/licenses", ct);
            License = lics?
                .OrderByDescending(l => l.Status == LicenseStatus.Active)
                .ThenByDescending(l => l.IssuedUtc)
                .FirstOrDefault();
        }

        var usage = await api.GetAsync<List<UsageVm>>("/api/v1/usage", ct) ?? new();
        LatestUsage = usage.OrderByDescending(u => u.ReportedUtc).FirstOrDefault();

        Payments = await api.GetAsync<List<PaymentVm>>("/api/v1/payments", ct) ?? new();
        Tickets = await api.GetAsync<List<TicketVm>>("/api/v1/tickets", ct) ?? new();
    }
}
