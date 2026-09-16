using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

[Authorize(Roles = "Customer")]
public class LisanslarimModel(ApiClient api) : PageModel
{
    public CustomerLicenseVm? License { get; private set; }
    public List<CustomerLicenseVm> History { get; private set; } = new();
    public UsageVm? LatestUsage { get; private set; }
    public string? TokenDownloadJson { get; private set; }

    public int? BusinessId =>
        int.TryParse(User.FindFirst("business")?.Value, out var b) ? b : null;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (BusinessId is { } bid)
        {
            var lics = await api.GetAsync<List<CustomerLicenseVm>>("/api/v1/my/licenses", ct) ?? new();
            History = lics.OrderByDescending(l => l.IssuedUtc).ToList();
            License = History
                .OrderByDescending(l => l.Status == LicenseStatus.Active)
                .ThenByDescending(l => l.IssuedUtc)
                .FirstOrDefault();
        }

        var usage = await api.GetAsync<List<UsageVm>>("/api/v1/usage", ct) ?? new();
        LatestUsage = usage.OrderByDescending(u => u.ReportedUtc).FirstOrDefault();
    }

    /// <summary>Lisans token'ını .lic dosyası olarak indir (Admin GET ile tam kayıttan token okunur).</summary>
    public async Task<IActionResult> OnGetDownloadAsync(int id, CancellationToken ct)
    {
        var lic = await api.GetAsync<LicenseFullVm>($"/api/v1/my/licenses/{id}", ct);
        if (lic is null || string.IsNullOrWhiteSpace(lic.LicenseToken))
            return NotFound("Lisans token'ı şu an indirilemiyor. Lütfen destek ekibiyle iletişime geçin.");

        var bytes = System.Text.Encoding.UTF8.GetBytes(lic.LicenseToken);
        var name = string.IsNullOrWhiteSpace(lic.LicenseCode) ? "lisans" : lic.LicenseCode;
        return File(bytes, "application/octet-stream", $"MindAeon-{name}.lic");
    }

    /// <summary>htmx: yükseltme talebi gönder (kısmi sonuç döndürür).</summary>
    public async Task<IActionResult> OnPostUpgradeAsync(int package, CancellationToken ct)
    {
        // Müşteri için en güvenli/uyumlu kanal: destek talebi (ticket) olarak yükseltme isteği.
        // (RequestsController POST yalnız Bayi'ye açık; müşteri yükseltmeyi destek üzerinden iletir.)
        var pkgName = package switch { 0 => "Lite", 1 => "Standart", 2 => "Enterprise", _ => $"#{package}" };
        var (ok, error) = await api.PostResultAsync("/api/v1/tickets", new
        {
            subject = $"Lisans Yükseltme Talebi — {pkgName}",
            body = $"Mevcut lisansımı {pkgName} paketine yükseltmek istiyorum. Lütfen fiyat ve süreç hakkında bilgilendirir misiniz?",
            priority = (int)TicketPriority.High
        }, ct);

        if (!ok) ViewData["UpgradeError"] = error;
        return Partial("_UpgradeResult", ok);
    }
}
