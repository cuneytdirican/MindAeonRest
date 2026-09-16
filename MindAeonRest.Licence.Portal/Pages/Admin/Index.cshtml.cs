using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public AdminDashboard Data { get; private set; } = Empty();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Tek bir dashboard ucu yok; mevcut uçlardan toplulaştırılır.
        var dealers = await _api.GetAsync<List<DealerListItem>>("/api/v1/dealers?includeInactive=true", ct) ?? [];
        var businesses = await _api.GetAsync<List<BusinessListItem>>("/api/v1/businesses", ct) ?? [];
        var licenses = await _api.GetAsync<List<LicenseListItem>>("/api/v1/admin/licenses", ct) ?? [];
        var requests = await _api.GetAsync<List<RequestListItem>>("/api/v1/requests?status=0", ct) ?? [];
        var tickets = await _api.GetAsync<List<TicketListItem>>("/api/v1/tickets", ct) ?? [];
        var payments = await _api.GetAsync<List<PaymentItem>>("/api/v1/payments", ct) ?? [];

        var paid = payments.Where(p => p.Status == PaymentStatus.Paid).ToList();
        var currency = paid.GroupBy(p => p.CurrencyCode).OrderByDescending(g => g.Count())
                           .Select(g => g.Key).FirstOrDefault() ?? "TRY";

        // Paket dağılımı (aktif lisanslar)
        var dist = licenses
            .Where(l => l.Status == LicenseStatus.Active)
            .GroupBy(l => l.PackageName)
            .Select(g => new PackageSlice(string.IsNullOrEmpty(g.Key) ? AdminLabels.Package(g.First().Package) : g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ToList();

        // Ciro/marj trendi — son 6 ay (tahsil edilen ödemelerin PaidUtc'sine göre)
        var now = DateTime.UtcNow;
        var trend = new List<TrendPoint>();
        for (int i = 5; i >= 0; i--)
        {
            var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            var inMonth = paid.Where(p => (p.PaidUtc ?? p.CreatedUtc).Year == month.Year
                                       && (p.PaidUtc ?? p.CreatedUtc).Month == month.Month).ToList();
            trend.Add(new TrendPoint(
                month.ToString("yyyy-MM"),
                inMonth.Sum(p => p.Amount),
                inMonth.Sum(p => p.MarginAmount),
                inMonth.Sum(p => p.NetAmount)));
        }

        // Son aktivite akışı — talepler + ödemeler birleşik
        var activity = new List<ActivityRow>();
        foreach (var r in requests.Take(6))
            activity.Add(new ActivityRow("request", $"{r.CompanyCode} — {r.RequestedPackageName}",
                "Lisans talebi", r.CreatedUtc, "warning"));
        foreach (var p in payments.OrderByDescending(p => p.CreatedUtc).Take(6))
            activity.Add(new ActivityRow("payment",
                $"{p.Amount:N2} {p.CurrencyCode} — {AdminLabels.Provider(p.Provider)}",
                AdminLabels.Payment(p.Status).Text,
                p.PaidUtc ?? p.CreatedUtc,
                p.Status == PaymentStatus.Paid ? "success" : "info"));
        activity = activity.OrderByDescending(a => a.WhenUtc).Take(8).ToList();

        Data = new AdminDashboard(
            TotalDealers: dealers.Count(d => d.IsActive),
            TotalBusinesses: businesses.Count,
            ActiveLicenses: licenses.Count(l => l.Status == LicenseStatus.Active),
            PendingRequests: requests.Count,
            OpenTickets: tickets.Count(t => t.Status != TicketStatus.Closed),
            GrossPaid: paid.Sum(p => p.Amount),
            MarginPaid: paid.Sum(p => p.MarginAmount),
            NetPaid: paid.Sum(p => p.NetAmount),
            PendingGross: payments.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.Amount),
            Currency: currency,
            PackageDistribution: dist,
            RevenueTrend: trend,
            RecentActivity: activity);
    }

    private static AdminDashboard Empty() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, "TRY", [], [], []);
}
