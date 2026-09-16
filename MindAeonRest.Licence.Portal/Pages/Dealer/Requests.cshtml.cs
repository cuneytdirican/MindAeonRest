using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class RequestsModel(ApiClient api) : PageModel
{
    public List<RequestListItemVm> Requests { get; private set; } = [];
    public List<BusinessListItemVm> Businesses { get; private set; } = [];

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    [BindProperty] public int? ReqBusinessId { get; set; }
    [BindProperty] public string ReqCompanyCode { get; set; } = "";
    [BindProperty] public string ReqHardwareId { get; set; } = "";
    [BindProperty] public int ReqPackage { get; set; } = 1;

    public int PendingCount => Requests.Count(r => r.Status == 0);
    public int IssuedCount => Requests.Count(r => r.Status == 1);
    public int RejectedCount => Requests.Count(r => r.Status == 2);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        Requests = await api.GetAsync<List<RequestListItemVm>>("/api/v1/requests/mine", ct) ?? [];
        Businesses = await api.GetAsync<List<BusinessListItemVm>>("/api/v1/businesses", ct) ?? [];
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
