using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Portal.Models;

// ============================================================================
// Admin portal view-model record'ları — API yanıtlarını (camelCase JSON) okur.
// API DTO'larıyla alan-alan eşleşir; Portal kendi kopyasını tutar (referans gerekmeden
// deserialize için yeterli). Enum'lar Data projesinden gelir (Portal zaten referanslı).
// ============================================================================

// ---- Dashboard ----
public record AdminDashboard(
    int TotalDealers,
    int TotalBusinesses,
    int ActiveLicenses,
    int PendingRequests,
    int OpenTickets,
    decimal GrossPaid,
    decimal MarginPaid,
    decimal NetPaid,
    decimal PendingGross,
    string Currency,
    IReadOnlyList<PackageSlice> PackageDistribution,
    IReadOnlyList<TrendPoint> RevenueTrend,
    IReadOnlyList<ActivityRow> RecentActivity);

public record PackageSlice(string Name, int Count);
public record TrendPoint(string Period, decimal Gross, decimal Margin, decimal Net);
public record ActivityRow(string Kind, string Title, string Subtitle, DateTime WhenUtc, string Tone);

// ---- Lisans ----
public record LicenseListItem(
    int Id,
    string LicenseCode,
    int Package,
    string PackageName,
    LicenseStatus Status,
    int? DealerId,
    string? DealerName,
    int BusinessId,
    string? BusinessTitle,
    string CompanyCode,
    string HardwareId,
    int MaxBranches,
    int MaxUsers,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc);

public record LicenseHistoryItem(
    int Id,
    LicenseAction Action,
    int? FromPackage,
    int? ToPackage,
    string? Note,
    DateTime CreatedUtc);

public record LicenseDetail(
    int Id,
    string LicenseCode,
    int Package,
    LicenseStatus Status,
    int? DealerId,
    int BusinessId,
    string HardwareId,
    int MaxBranches,
    int MaxUsers,
    long FeatureBits,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc,
    string? Notes,
    IReadOnlyList<LicenseHistoryItem>? History);

// ---- Talepler ----
public record RequestListItem(
    int Id,
    int? BusinessId,
    int? DealerId,
    string CompanyCode,
    string HardwareId,
    int RequestedPackage,
    string RequestedPackageName,
    RequestStatus Status,
    int? IssuedLicenseId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

// ---- İşletmeler ----
public record BusinessListItem(
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

// ---- Add-on katalog (lisans üretim formu) ----
public record AddOnCatalogItem(long Bit, string Name);

// ---- Add-on fiyatları ----
public record AddOnPriceItem(
    int Id,
    int Kind,
    long FeatureBit,
    string Name,
    decimal UnitPrice,
    string CurrencyCode,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

// ---- Bayiler ----
public record DealerListItem(
    int Id,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    decimal MarginPercent,
    BillingMode BillingMode,
    bool IsActive,
    int BusinessCount,
    int LicenseCount,
    DateTime CreatedUtc);

public record DealerDetail(
    int Id,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    decimal MarginPercent,
    BillingMode BillingMode,
    bool IsActive,
    string? Notes,
    int BusinessCount,
    int LicenseCount,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

public record DealerEarnings(
    int DealerId,
    string DealerName,
    string CurrencyCode,
    int PaidCount,
    decimal GrossTotal,
    decimal MarginTotal,
    decimal NetTotal,
    int PendingCount,
    decimal PendingGross);

// ---- Paket fiyatları ----
public record PackagePriceItem(
    int Id,
    int Package,
    string PackageName,
    DurationKind Duration,
    decimal BasePrice,
    string CurrencyCode,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

// ---- Ödemeler ----
public record PaymentItem(
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

// ---- Demolar ----
public record DemoItem(
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

// ---- Kullanım ----
public record UsageItem(
    int Id,
    int? BusinessId,
    string CompanyCode,
    DateTime ReportedUtc,
    int BranchCount,
    int UserCount,
    int KioskCount,
    decimal Revenue,
    string? Period);

// ---- Destek (ticket) ----
public record TicketListItem(
    int Id,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    int? DealerId,
    int? BusinessId,
    string OpenerUserId,
    string? AssignedToUserId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    DateTime? ClosedUtc,
    int MessageCount);

public record TicketMessageItem(
    int Id,
    string AuthorUserId,
    string Body,
    bool IsInternal,
    DateTime CreatedUtc);

public record TicketDetail(
    int Id,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    int? DealerId,
    int? BusinessId,
    string OpenerUserId,
    string? AssignedToUserId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    DateTime? ClosedUtc,
    IReadOnlyList<TicketMessageItem> Messages);

// ============================================================================
// UI yardımcıları — durum/öncelik/sağlayıcı için Türkçe etiket + badge sınıfı.
// ============================================================================
public static class AdminLabels
{
    public static (string Text, string Css) License(LicenseStatus s) => s switch
    {
        LicenseStatus.Trial => ("Deneme", "info"),
        LicenseStatus.Active => ("Aktif", "success"),
        LicenseStatus.Expired => ("Süresi Doldu", "warning"),
        LicenseStatus.Upgraded => ("Yükseltildi", "primary"),
        LicenseStatus.Revoked => ("İptal", "danger"),
        _ => (s.ToString(), "")
    };

    /// <summary>POS tenant lisans durumu (provisioning tenant listesi; POS LicenseState enum adı metin olarak gelir).</summary>
    public static (string Text, string Css) PosLicense(string? state) => state switch
    {
        "Licensed" => ("Lisanslı", "success"),
        "Trial" => ("Deneme", "info"),
        "TrialExpired" => ("Deneme Bitti", "danger"),
        "Expired" => ("Süresi Doldu", "warning"),
        "Invalid" => ("Geçersiz", "danger"),
        "WrongMachine" => ("Yanlış Makine", "danger"),
        null or "" => ("—", ""),
        _ => (state, "")
    };

    public static (string Text, string Css) Request(RequestStatus s) => s switch
    {
        RequestStatus.Pending => ("Bekliyor", "warning"),
        RequestStatus.Issued => ("Üretildi", "success"),
        RequestStatus.Rejected => ("Reddedildi", "danger"),
        _ => (s.ToString(), "")
    };

    public static (string Text, string Css) Payment(PaymentStatus s) => s switch
    {
        PaymentStatus.Pending => ("Bekliyor", "warning"),
        PaymentStatus.Paid => ("Tahsil Edildi", "success"),
        PaymentStatus.Failed => ("Başarısız", "danger"),
        PaymentStatus.Refunded => ("İade", "info"),
        PaymentStatus.Cancelled => ("İptal", ""),
        _ => (s.ToString(), "")
    };

    public static (string Text, string Css) Ticket(TicketStatus s) => s switch
    {
        TicketStatus.Open => ("Açık", "info"),
        TicketStatus.InProgress => ("İşlemde", "primary"),
        TicketStatus.Answered => ("Yanıtlandı", "success"),
        TicketStatus.Closed => ("Kapalı", ""),
        _ => (s.ToString(), "")
    };

    public static (string Text, string Css) Priority(TicketPriority p) => p switch
    {
        TicketPriority.Low => ("Düşük", ""),
        TicketPriority.Normal => ("Normal", "info"),
        TicketPriority.High => ("Yüksek", "warning"),
        TicketPriority.Urgent => ("Acil", "danger"),
        _ => (p.ToString(), "")
    };

    public static string Provider(PaymentProviderKind p) => p switch
    {
        PaymentProviderKind.Iyzico => "iyzico",
        PaymentProviderKind.PayTR => "PayTR",
        PaymentProviderKind.GarantiVPos => "Garanti VPOS",
        PaymentProviderKind.BankTransfer => "Banka Havalesi",
        _ => p.ToString()
    };

    public static string Billing(BillingMode b) => b switch
    {
        BillingMode.CustomerPaysUs => "Müşteri öder",
        BillingMode.DealerPaysUs => "Bayi öder",
        _ => b.ToString()
    };

    public static string Duration(DurationKind d) => d switch
    {
        DurationKind.Perpetual => "Süresiz",
        DurationKind.Monthly => "Aylık",
        DurationKind.Yearly => "Yıllık",
        _ => d.ToString()
    };

    public static string Action(LicenseAction a) => a switch
    {
        LicenseAction.Issued => "Üretildi",
        LicenseAction.Upgraded => "Yükseltildi",
        LicenseAction.Renewed => "Yenilendi",
        LicenseAction.Revoked => "İptal edildi",
        _ => a.ToString()
    };

    public static string Package(int p) => p switch
    {
        0 => "Lite",
        1 => "Standart",
        2 => "Enterprise",
        _ => $"#{p}"
    };

    // Add-on fiyat türü: 0=Module, 1=User, 2=Kiosk, 3=Branch
    public static (string Text, string Css) AddOnKind(int kind) => kind switch
    {
        0 => ("Modül", "primary"),
        1 => ("Kullanıcı", "info"),
        2 => ("Terminal/Kiosk", "warning"),
        3 => ("Şube", "success"),
        _ => ($"#{kind}", "")
    };
}
