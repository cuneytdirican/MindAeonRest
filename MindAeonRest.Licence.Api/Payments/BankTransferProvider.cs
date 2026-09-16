using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Payments;

/// <summary>
/// Manuel banka havalesi/EFT sağlayıcısı (TAM uygulama).
/// Online tahsilat yapmaz: ödeme başlatıldığında <see cref="PaymentStatus.Pending"/> döner ve
/// müşteriye havale yapılacak hesap bilgisi (config'teki IbanInfo) gösterilir.
/// Ödemeyi Admin, dekontu eşleştirip <c>PaymentsController.ApproveBankTransfer</c> ile
/// <see cref="PaymentStatus.Paid"/> yapar (3DS/webhook YOK).
/// </summary>
public sealed class BankTransferProvider : IPaymentProvider
{
    private readonly ProviderCredentials? _cfg;

    public BankTransferProvider(ProviderCredentials? cfg)
    {
        _cfg = cfg;
    }

    public PaymentProviderKind Kind => PaymentProviderKind.BankTransfer;

    public Task<CreateResult> CreateAsync(decimal amount, string currency, string? returnUrl, PaymentMeta meta)
    {
        // Manuel akış: kayıt Pending kalır; ödeme bilgisini (IBAN/açıklama) kullanıcıya bildir.
        var iban = string.IsNullOrWhiteSpace(_cfg?.IbanInfo)
            ? "Havale hesabı bilgisi yapılandırılmamış; lütfen yetkiliyle iletişime geçin."
            : _cfg!.IbanInfo!;

        var msg = $"Havale/EFT ile {amount:0.00} {currency} ödeyiniz. Hesap: {iban}. " +
                  $"Açıklama kısmına ödeme no '#{meta.PaymentId}' yazınız. Onay sonrası lisansınız etkinleşir.";

        return Task.FromResult(CreateResult.Pending(msg));
    }

    // Manuel sağlayıcıda 3DS callback yoktur.
    public Task<VerifyResult> Handle3DCallbackAsync(IReadOnlyDictionary<string, string> form) =>
        Task.FromResult(VerifyResult.Reject("Banka havalesinde 3D Secure dönüşü yoktur; onay manuel yapılır."));

    // Webhook yoktur.
    public Task<bool> VerifyWebhookAsync(HttpRequest request) => Task.FromResult(false);

    // İade manueldir (geri havale); sistem otomatik iade yapmaz.
    public Task<RefundResult> RefundAsync(string providerRef, decimal amount) =>
        Task.FromResult(RefundResult.Fail("Banka havalesinde iade manuel (geri havale) yapılır; sistem otomatik iade desteklemez."));
}
