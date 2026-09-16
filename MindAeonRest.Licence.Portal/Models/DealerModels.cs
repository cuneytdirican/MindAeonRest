namespace MindAeonRest.Licence.Portal.Models;

// =====================================================================================
// Bayi (Dealer) portal sayfaları için view-model record'ları.
// API DTO'larıyla alan-alan eşleşir (ApiClient JSON yanıtını bunlara deserialize eder).
// Enum'lar int olarak okunur (JSON sayısal) — etiketleme View'da yapılır.
// Çakışma yok: bu dosya Portal/Models altında, Dealer ajanına ait.
// =====================================================================================

// ---- İşletme (Business) ----
public record BusinessListItemVm(
    int Id,
    int? DealerId,
    string? DealerName,
    string CompanyCode,
    string Title,
    string? City,
    string? Phone,
    string? Email,
    string? HardwareId,
    int ActiveLicenseCount,
    DateTime CreatedUtc);

public record BusinessDetailVm(
    int Id,
    int? DealerId,
    string? DealerName,
    string CompanyCode,
    string Title,
    string? TaxOffice,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Phone,
    string? Email,
    string? HardwareId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    List<BusinessLicenseInfoVm> Licenses);

public record BusinessLicenseInfoVm(
    int LicenseId,
    string LicenseCode,
    int Package,
    int MaxBranches,
    int MaxUsers,
    string HardwareId,
    int Status,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc,
    string? Notes,
    List<BusinessLicenseHistoryInfoVm> History);

public record BusinessLicenseHistoryInfoVm(
    int Id,
    int Action,
    int? FromPackage,
    int? ToPackage,
    string? Note,
    DateTime CreatedUtc);

public record CreateBusinessCustomerRespVm(
    string UserId,
    string Email,
    string FullName,
    int BusinessId);

// ---- Yükseltme / Lisans talepleri ----
public record RequestListItemVm(
    int Id,
    int? BusinessId,
    int? DealerId,
    string CompanyCode,
    string HardwareId,
    int RequestedPackage,
    string RequestedPackageName,
    int Status,
    int? IssuedLicenseId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

public record CreateRequestRespVm(int Id, int Status, string Message);

// ---- Demo kurulumları ----
public record DemoRespVm(
    int Id,
    int? DealerId,
    string CompanyCode,
    string? BusinessTitle,
    string? HardwareId,
    string? ContactName,
    string? Phone,
    string? Email,
    DateTime StartedUtc,
    bool Converted,
    int? LicenseId);

// ---- Kazançlar (marj) ----
public record DealerEarningsVm(
    int DealerId,
    string DealerName,
    string CurrencyCode,
    int PaidCount,
    decimal GrossTotal,
    decimal MarginTotal,
    decimal NetTotal,
    int PendingCount,
    decimal PendingGross);

// ---- Ödemeler ----
public record PaymentRespVm(
    int Id,
    int? DealerId,
    int? BusinessId,
    int? LicenseId,
    int Provider,
    int BillingMode,
    decimal Amount,
    decimal MarginAmount,
    decimal NetAmount,
    string CurrencyCode,
    int Status,
    string? ProviderRef,
    DateTime? PaidUtc,
    DateTime CreatedUtc);

// ---- Destek (Ticket) ----
public record TicketListItemVm(
    int Id,
    string Subject,
    int Status,
    int Priority,
    int? DealerId,
    int? BusinessId,
    string OpenerUserId,
    string? AssignedToUserId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    DateTime? ClosedUtc,
    int MessageCount);

public record TicketMessageRespVm(
    int Id,
    string AuthorUserId,
    string Body,
    bool IsInternal,
    DateTime CreatedUtc);

public record TicketDetailRespVm(
    int Id,
    string Subject,
    int Status,
    int Priority,
    int? DealerId,
    int? BusinessId,
    string OpenerUserId,
    string? AssignedToUserId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    DateTime? ClosedUtc,
    List<TicketMessageRespVm> Messages);

// ---- Ortak yardımcı etiketler (View'larda paylaşılır) ----
public static class DealerLabels
{
    public static string Package(int p) => p switch
    {
        0 => "Lite",
        1 => "Standart",
        2 => "Enterprise",
        _ => $"Paket {p}"
    };

    public static (string Text, string Css) LicenseStatus(int s) => s switch
    {
        0 => ("Deneme", "warning"),
        1 => ("Aktif", "success"),
        2 => ("Süresi Doldu", "danger"),
        3 => ("Yükseltildi", "info"),
        4 => ("İptal", "danger"),
        _ => ("Bilinmiyor", "")
    };

    public static (string Text, string Css) RequestStatus(int s) => s switch
    {
        0 => ("Bekliyor", "warning"),
        1 => ("Onaylandı", "success"),
        2 => ("Reddedildi", "danger"),
        _ => ("Bilinmiyor", "")
    };

    public static (string Text, string Css) PaymentStatus(int s) => s switch
    {
        0 => ("Bekliyor", "warning"),
        1 => ("Ödendi", "success"),
        2 => ("Başarısız", "danger"),
        3 => ("İade", "info"),
        4 => ("İptal", ""),
        _ => ("Bilinmiyor", "")
    };

    public static string Provider(int p) => p switch
    {
        0 => "iyzico",
        1 => "PayTR",
        2 => "Garanti VPOS",
        3 => "Banka Havalesi",
        _ => $"Sağlayıcı {p}"
    };

    public static (string Text, string Css) TicketStatus(int s) => s switch
    {
        0 => ("Açık", "info"),
        1 => ("İşlemde", "warning"),
        2 => ("Yanıtlandı", "success"),
        3 => ("Kapalı", ""),
        _ => ("Bilinmiyor", "")
    };

    public static (string Text, string Css) TicketPriority(int p) => p switch
    {
        0 => ("Düşük", ""),
        1 => ("Normal", "info"),
        2 => ("Yüksek", "warning"),
        3 => ("Acil", "danger"),
        _ => ("—", "")
    };

    public static string BillingMode(int m) => m switch
    {
        0 => "Müşteri bize öder",
        1 => "Bayi bize öder",
        _ => "—"
    };
}
