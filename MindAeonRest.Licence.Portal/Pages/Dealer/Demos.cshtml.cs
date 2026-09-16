using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class DemosModel(ApiClient api) : PageModel
{
    public List<DemoRespVm> Demos { get; private set; } = [];

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    [BindProperty] public string CompanyCode { get; set; } = "";
    [BindProperty] public string? BusinessTitle { get; set; }
    [BindProperty] public string? HardwareId { get; set; }
    [BindProperty] public string? ContactName { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Email { get; set; }

    public int OpenCount => Demos.Count(d => !d.Converted);
    public int ConvertedCount => Demos.Count(d => d.Converted);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct) =>
        Demos = await api.GetAsync<List<DemoRespVm>>("/api/v1/demos", ct) ?? [];

    public async Task<IActionResult> OnPostCreateDemoAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(CompanyCode))
        {
            // Şirket kodu zorunlu; verilmediyse otomatik üret (elle giriş zorunlu değil).
            CompanyCode = "DEMO-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        }

        var body = new
        {
            dealerId = (int?)null, // bayi kendi tenant'ına yazılır (API zorlar)
            companyCode = CompanyCode.Trim(),
            businessTitle = BusinessTitle,
            hardwareId = HardwareId,
            contactName = ContactName,
            phone = Phone,
            email = Email
        };

        var (resp, error) = await api.PostResultAsync<DemoRespVm>("/api/v1/demos", body, ct);
        if (resp is null)
        {
            Flash = error ?? "Demo kaydı oluşturulamadı.";
            FlashKind = "danger";
        }
        else
        {
            Flash = $"Demo kuruldu: {resp.CompanyCode}.";
            FlashKind = "success";
        }
        return RedirectToPage();
    }
}
