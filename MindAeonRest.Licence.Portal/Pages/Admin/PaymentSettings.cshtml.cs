using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

/// <summary>
/// Ödeme Ayarları / Havale Hesabı — Admin, havale (IBAN) hesap bilgisini görür ve düzenler.
/// Yalnız Admin erişebilir. Veri, typed ApiClient üzerinden JWT bearer ile API'den gelir/gider.
/// </summary>
[Authorize(Roles = "Admin")]
public class PaymentSettingsModel(ApiClient api) : PageModel
{
    [BindProperty] public string? Iban { get; set; }
    [BindProperty] public string? AccountName { get; set; }
    [BindProperty] public string? BankName { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public bool Active { get; set; }

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var cfg = await api.GetBankTransferConfigAsync(ct);
        if (cfg is not null)
        {
            Iban = cfg.Iban;
            AccountName = cfg.AccountName;
            BankName = cfg.BankName;
            Description = cfg.Description;
            Active = cfg.Active;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Iban) || string.IsNullOrWhiteSpace(AccountName))
        {
            Flash = "IBAN ve hesap adı zorunludur.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        var dto = new BankTransferConfig(
            Iban?.Trim(),
            AccountName?.Trim(),
            BankName?.Trim(),
            Description?.Trim(),
            Active);

        var (ok, error) = await api.SaveBankTransferConfigAsync(dto, ct);
        Flash = ok ? "Havale hesabı kaydedildi." : (error ?? "Kayıt başarısız.");
        FlashKind = ok ? "success" : "danger";
        return RedirectToPage();
    }
}
