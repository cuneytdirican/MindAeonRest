using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

[Authorize(Roles = "Customer")]
public class KullanimimModel(ApiClient api) : PageModel
{
    public List<UsageVm> Reports { get; private set; } = new();
    public CustomerLicenseVm? License { get; private set; }

    public UsageVm? Latest => Reports.FirstOrDefault();

    public int? BusinessId =>
        int.TryParse(User.FindFirst("business")?.Value, out var b) ? b : null;

    public decimal RevenueTrend
    {
        get
        {
            if (Reports.Count < 2) return 0m;
            var cur = Reports[0].Revenue;
            var prev = Reports[1].Revenue;
            if (prev == 0) return cur > 0 ? 100m : 0m;
            return Math.Round((cur - prev) / prev * 100m, 1);
        }
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Reports = (await api.GetAsync<List<UsageVm>>("/api/v1/usage", ct) ?? new())
            .OrderByDescending(u => u.ReportedUtc).ToList();

        if (BusinessId is { } bid)
        {
            var lics = await api.GetAsync<List<CustomerLicenseVm>>("/api/v1/my/licenses", ct) ?? new();
            License = lics
                .OrderByDescending(l => l.Status == LicenseStatus.Active)
                .ThenByDescending(l => l.IssuedUtc)
                .FirstOrDefault();
        }
    }

    /// <summary>Limit aşımı/yaklaşımı uyarıları (lisans varsa).</summary>
    public IEnumerable<(string Kind, string Text)> LimitWarnings()
    {
        if (License is null || Latest is null) yield break;

        if (License.MaxBranches != int.MaxValue && License.MaxBranches > 0)
        {
            if (Latest.BranchCount > License.MaxBranches)
                yield return ("danger", $"Şube sayısı limiti aşıldı: {Latest.BranchCount} / {License.MaxBranches}.");
            else if (Latest.BranchCount == License.MaxBranches)
                yield return ("warning", $"Şube limitine ulaşıldı: {Latest.BranchCount} / {License.MaxBranches}.");
        }

        if (License.MaxUsers != int.MaxValue && License.MaxUsers > 0)
        {
            if (Latest.UserCount > License.MaxUsers)
                yield return ("danger", $"Kullanıcı sayısı limiti aşıldı: {Latest.UserCount} / {License.MaxUsers}.");
            else if (Latest.UserCount >= License.MaxUsers)
                yield return ("warning", $"Kullanıcı limitine ulaşıldı: {Latest.UserCount} / {License.MaxUsers}.");
        }
    }
}
