using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Payments;

/// <summary>
/// Pluggable ödeme sağlayıcı arayüzü. Her sağlayıcı (İyzico/PayTR/Garanti/Havale)
/// bu arayüzü uygular. Tutar/marj hesabı SAĞLAYICIDA DEĞİL controller'da yapılır;
/// sağlayıcıya yalnızca kesinleşmiş brüt tutar verilir.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Bu örneğin temsil ettiği sağlayıcı türü.</summary>
    PaymentProviderKind Kind { get; }

    /// <summary>
    /// Ödeme/işlem başlatır. 3DS gerektiren sağlayıcılarda RedirectUrl veya Html (3DS formu)
    /// döner; manuel sağlayıcılar (havale) Pending döner.
    /// </summary>
    Task<CreateResult> CreateAsync(decimal amount, string currency, string? returnUrl, PaymentMeta meta);

    /// <summary>
    /// 3DS dönüş (callback) form alanlarını doğrular. İmza/hash kontrolü sağlayıcıya özgüdür.
    /// Başarılıysa ProviderRef ile birlikte Approved döner.
    /// </summary>
    Task<VerifyResult> Handle3DCallbackAsync(IReadOnlyDictionary<string, string> form);

    /// <summary>
    /// Sunucu-sunucu webhook bildirimini doğrular (imza/hmac). Geçerliyse true döner.
    /// </summary>
    Task<bool> VerifyWebhookAsync(HttpRequest request);

    /// <summary>İade (kısmi/tam) işlemi başlatır.</summary>
    Task<RefundResult> RefundAsync(string providerRef, decimal amount);
}

/// <summary>Sağlayıcıya iletilen iş bağlamı (loglama/iz/3DS meta için).</summary>
public sealed record PaymentMeta(
    int PaymentId,
    int? DealerId,
    int? BusinessId,
    int? LicenseId,
    int Package,
    DurationKind Duration,
    string? BuyerEmail);

/// <summary>Ödeme başlatma sonucu.</summary>
/// <param name="Status">Pending=manuel/bekliyor, Paid=anında, Failed=hata.</param>
/// <param name="ProviderRef">Sağlayıcı işlem kimliği (varsa).</param>
/// <param name="RedirectUrl">3DS yönlendirme URL'i (varsa).</param>
/// <param name="Html">3DS otomatik gönderim formu HTML'i (varsa).</param>
public sealed record CreateResult(
    PaymentStatus Status,
    string? ProviderRef,
    string? RedirectUrl = null,
    string? Html = null,
    string? Message = null)
{
    public static CreateResult Pending(string? msg = null) => new(PaymentStatus.Pending, null, Message: msg);
    public static CreateResult Redirect(string url, string? providerRef = null) => new(PaymentStatus.Pending, providerRef, RedirectUrl: url);
    public static CreateResult Form(string html, string? providerRef = null) => new(PaymentStatus.Pending, providerRef, Html: html);
    public static CreateResult Fail(string msg) => new(PaymentStatus.Failed, null, Message: msg);
}

/// <summary>3DS/callback doğrulama sonucu.</summary>
public sealed record VerifyResult(
    bool Approved,
    string? ProviderRef,
    string? Message = null)
{
    public static VerifyResult Ok(string providerRef) => new(true, providerRef);
    public static VerifyResult Reject(string msg) => new(false, null, msg);
}

/// <summary>İade sonucu.</summary>
public sealed record RefundResult(
    bool Success,
    string? ProviderRef,
    string? Message = null)
{
    public static RefundResult Ok(string providerRef) => new(true, providerRef);
    public static RefundResult Fail(string msg) => new(false, null, msg);
}
