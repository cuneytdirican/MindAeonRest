using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class PaymentsModel(ApiClient api) : PageModel
{
    public List<PaymentRespVm> Payments { get; private set; } = [];

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    // Marj/ciro özetleri
    public decimal MarginPaid { get; private set; }
    public decimal MarginPending { get; private set; }
    public decimal GrossPaid { get; private set; }
    public decimal NetPaid { get; private set; }
    public string Currency { get; private set; } = "TRY";

    // BillingMode: bayinin ödemelerinden çıkar (DealerPaysUs=1 ise "Öde" aksiyonu görünür).
    public bool DealerPays { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        Payments = await api.GetAsync<List<PaymentRespVm>>("/api/v1/payments", ct) ?? [];

        var paid = Payments.Where(p => p.Status == 1).ToList();   // Paid
        var pending = Payments.Where(p => p.Status == 0).ToList(); // Pending
        MarginPaid = paid.Sum(p => p.MarginAmount);
        MarginPending = pending.Sum(p => p.MarginAmount);
        GrossPaid = paid.Sum(p => p.Amount);
        NetPaid = paid.Sum(p => p.NetAmount);
        Currency = Payments.FirstOrDefault()?.CurrencyCode ?? "TRY";

        // BillingMode==1 (DealerPaysUs) varsa bayi bize ödeme yapar.
        DealerPays = Payments.Any(p => p.BillingMode == 1);
    }

    /// <summary>DealerPaysUs modelinde bekleyen bir ödemeyi banka havalesiyle yeniden başlat (ödeme akışı).</summary>
    public async Task<IActionResult> OnPostPayAsync(int paymentId, CancellationToken ct)
    {
        // Mevcut bekleyen ödemenin paket/işletme bağlamından yeni bir ödeme başlatılır.
        // (Var olan Payment Pending kalır; sağlayıcı akışı StartPayment ile yürür.)
        var src = (await api.GetAsync<List<PaymentRespVm>>("/api/v1/payments", ct) ?? [])
            .FirstOrDefault(p => p.Id == paymentId);

        if (src is null)
        {
            Flash = "Ödeme bulunamadı.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        // Banka havalesi sağlayıcısı ile başlat (talimat/HTML döner).
        var body = new
        {
            provider = 3, // BankTransfer
            package = 1,  // Standart (varsayılan; gerçek paket lisanstan türetilebilir)
            duration = 0, // Perpetual
            businessId = src.BusinessId,
            licenseId = src.LicenseId,
            returnUrl = (string?)null
        };

        var (resp, error) = await api.PostResultAsync<StartPaymentRespVm>("/api/v1/payments/start", body, ct);
        if (resp is null)
        {
            Flash = error ?? "Ödeme başlatılamadı. Lütfen yöneticiyle iletişime geçin.";
            FlashKind = "danger";
        }
        else
        {
            Flash = resp.Message ?? "Ödeme talimatı oluşturuldu. Havale onayı sonrası kapanır.";
            FlashKind = "success";
        }
        return RedirectToPage();
    }
}

/// <summary>Ödeme başlatma yanıtı (API StartPaymentResp ile eşleşir).</summary>
public record StartPaymentRespVm(
    int PaymentId,
    int Status,
    int Provider,
    decimal Amount,
    decimal MarginAmount,
    decimal NetAmount,
    string CurrencyCode,
    string? ProviderRef,
    string? RedirectUrl,
    string? Html,
    string? Message);
