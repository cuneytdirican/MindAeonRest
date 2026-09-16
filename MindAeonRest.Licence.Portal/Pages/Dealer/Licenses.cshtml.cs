using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class LicensesModel(ApiClient api) : PageModel
{
    /// <summary>Bir lisansı, ait olduğu işletme bilgisiyle birlikte taşır (salt-okuma görünüm).</summary>
    public record LicenseRow(string BizTitle, string CompanyCode, string? HardwareId, BusinessLicenseInfoVm License);

    public List<LicenseRow> Rows { get; private set; } = [];
    public List<BusinessListItemVm> Businesses { get; private set; } = [];

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    // ---- Yükseltme talebi alanları ----
    [BindProperty] public int? ReqBusinessId { get; set; }
    [BindProperty] public string ReqCompanyCode { get; set; } = "";
    [BindProperty] public string ReqHardwareId { get; set; } = "";
    [BindProperty] public int ReqPackage { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        Businesses = await api.GetAsync<List<BusinessListItemVm>>("/api/v1/businesses", ct) ?? [];

        var rows = new List<LicenseRow>();
        foreach (var b in Businesses)
        {
            var detail = await api.GetAsync<BusinessDetailVm>($"/api/v1/businesses/{b.Id}", ct);
            if (detail?.Licenses is null) continue;
            foreach (var lic in detail.Licenses)
                rows.Add(new LicenseRow(detail.Title, detail.CompanyCode, detail.HardwareId, lic));
        }
        Rows = rows
            .OrderByDescending(r => r.License.IssuedUtc)
            .ToList();
    }

    public async Task<IActionResult> OnPostRequestAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ReqCompanyCode) || string.IsNullOrWhiteSpace(ReqHardwareId))
        {
            Flash = "Şirket kodu ve donanım kimliği zorunludur.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        var body = new
        {
            businessId = ReqBusinessId,
            companyCode = ReqCompanyCode.Trim(),
            hardwareId = ReqHardwareId.Trim(),
            requestedPackage = ReqPackage
        };

        var (resp, error) = await api.PostResultAsync<CreateRequestRespVm>("/api/v1/requests", body, ct);
        if (resp is null)
        {
            Flash = error ?? "Talep gönderilemedi.";
            FlashKind = "danger";
        }
        else
        {
            Flash = resp.Message;
            FlashKind = "success";
        }
        return RedirectToPage();
    }
}
