using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

// =====================================================================================
// Ödeme DTO'ları. Tutar ASLA istemciden alınmaz — sunucuda PackagePrice + Dealer marjı
// üzerinden hesaplanır. İstemci yalnızca NE ödeneceğini (paket/süre + hedef) bildirir.
// =====================================================================================

/// <summary>
/// Ödeme başlatma isteği. DİKKAT: Tutar istemciden GELMEZ; sunucu PackagePrice (paket+süre)
/// ve hedef bayinin MarginPercent değeriyle brüt/marj/net hesaplar.
/// </summary>
public record StartPaymentReq(
    PaymentProviderKind Provider,
    int Package,
    DurationKind Duration,
    int? BusinessId,
    int? LicenseId,
    string? ReturnUrl);

/// <summary>Ödeme başlatma yanıtı. RedirectUrl/Html sağlayıcıya göre 3DS yönlendirmesi içerir.</summary>
public record StartPaymentResp(
    int PaymentId,
    PaymentStatus Status,
    PaymentProviderKind Provider,
    decimal Amount,
    decimal MarginAmount,
    decimal NetAmount,
    string CurrencyCode,
    string? ProviderRef,
    string? RedirectUrl,
    string? Html,
    string? Message);

/// <summary>Ödeme listesi satırı (tenant bazlı).</summary>
public record PaymentResp(
    int Id,
    int? DealerId,
    int? BusinessId,
    int? LicenseId,
    PaymentProviderKind Provider,
    BillingMode BillingMode,
    decimal Amount,
    decimal MarginAmount,
    decimal NetAmount,
    string CurrencyCode,
    PaymentStatus Status,
    string? ProviderRef,
    DateTime? PaidUtc,
    DateTime CreatedUtc);

/// <summary>BankTransferProvider için admin onay isteği (manuel havale eşleştirme).</summary>
public record ApproveBankTransferReq(string? ProviderRef, string? Note);

/// <summary>
/// Banka Havalesi/EFT yapılandırması (Admin yönetim ucu). GET yanıtı + POST isteği için ortak şema.
/// Şifreli PaymentProviderConfig (global, DealerId=null) ile eşlenir; <c>Active</c> config.IsActive'e karşılık gelir.
/// </summary>
public record BankTransferConfigDto(
    string? Iban,
    string? AccountName,
    string? BankName,
    string? Description,
    bool Active);
