using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Payments;

/// <summary>
/// Online (kredi kartı / 3D Secure) tahsilat yapan PSP sağlayıcıları için fail-closed sağlayıcı.
/// İyzico/PayTR/Garanti VPOS gibi yöntemler şu an etkin değildir: hiçbir sahte başarı/yönlendirme
/// üretmez, her zaman net bir "bu ödeme yöntemi etkin değil" sonucu döner.
///
/// Bu yöntemler müşteri arayüzünde seçilemez (yalnızca Banka Havalesi/EFT sunulur). Sağlayıcı
/// yine de mevcut DB satırlarının (PaymentProviderKind) çözülebilmesi ve herhangi bir kod yolunun
/// yanlışlıkla buraya ulaşması durumunda güvenli (fail-closed) kalması için tutulur.
/// </summary>
public sealed class UnavailableOnlineProvider : IPaymentProvider
{
    private const string Unavailable = "Bu ödeme yöntemi şu an etkin değil. Lütfen Banka Havalesi/EFT kullanın.";

    public UnavailableOnlineProvider(PaymentProviderKind kind)
    {
        Kind = kind;
    }

    public PaymentProviderKind Kind { get; }

    public Task<CreateResult> CreateAsync(decimal amount, string currency, string? returnUrl, PaymentMeta meta) =>
        Task.FromResult(CreateResult.Fail(Unavailable));

    public Task<VerifyResult> Handle3DCallbackAsync(IReadOnlyDictionary<string, string> form) =>
        Task.FromResult(VerifyResult.Reject(Unavailable));

    public Task<bool> VerifyWebhookAsync(HttpRequest request) => Task.FromResult(false);

    public Task<RefundResult> RefundAsync(string providerRef, decimal amount) =>
        Task.FromResult(RefundResult.Fail(Unavailable));
}
