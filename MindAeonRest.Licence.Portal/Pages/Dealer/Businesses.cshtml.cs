using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class BusinessesModel(ApiClient api) : PageModel
{
    public List<BusinessListItemVm> Businesses { get; private set; } = [];

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    // ---- Yeni işletme alanları (CompanyCode otomatik üretilir) ----
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string? City { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? TaxOffice { get; set; }
    [BindProperty] public string? TaxNumber { get; set; }
    [BindProperty] public string? Address { get; set; }

    // ---- Müşteri hesabı alanları ----
    [BindProperty] public int CustBusinessId { get; set; }
    [BindProperty] public string CustEmail { get; set; } = "";
    [BindProperty] public string CustPassword { get; set; } = "";
    [BindProperty] public string? CustFullName { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct) =>
        Businesses = await api.GetAsync<List<BusinessListItemVm>>("/api/v1/businesses", ct) ?? [];

    public async Task<IActionResult> OnPostCreateBusinessAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Flash = "Unvan zorunludur.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        // CompanyCode otomatik: MA- + 8 haneli benzersiz kök (elle giriş yok).
        var companyCode = "MA-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var body = new
        {
            companyCode,
            title = Title.Trim(),
            dealerId = (int?)null, // bayi kendi DealerId'sine API tarafında bağlanır
            taxOffice = TaxOffice,
            taxNumber = TaxNumber,
            address = Address,
            city = City,
            phone = Phone,
            email = Email,
            hardwareId = (string?)null
        };

        var (created, error) = await api.PostResultAsync<BusinessDetailVm>("/api/v1/businesses", body, ct);
        if (created is null)
        {
            Flash = error ?? "İşletme oluşturulamadı (kod çakışması veya yetki).";
            FlashKind = "danger";
        }
        else
        {
            Flash = $"İşletme oluşturuldu: {created.Title} ({created.CompanyCode}).";
            FlashKind = "success";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateCustomerAsync(CancellationToken ct)
    {
        if (CustBusinessId <= 0 || string.IsNullOrWhiteSpace(CustEmail) || string.IsNullOrWhiteSpace(CustPassword))
        {
            Flash = "İşletme, e-posta ve parola zorunludur.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        var body = new { email = CustEmail.Trim(), password = CustPassword, fullName = CustFullName };
        var (created, error) = await api.PostResultAsync<CreateBusinessCustomerRespVm>(
            $"/api/v1/businesses/{CustBusinessId}/customer-account", body, ct);

        if (created is null)
        {
            Flash = error ?? "Müşteri hesabı oluşturulamadı (e-posta kullanımda olabilir).";
            FlashKind = "danger";
        }
        else
        {
            Flash = $"Müşteri hesabı oluşturuldu: {created.Email}.";
            FlashKind = "success";
        }
        return RedirectToPage();
    }
}
