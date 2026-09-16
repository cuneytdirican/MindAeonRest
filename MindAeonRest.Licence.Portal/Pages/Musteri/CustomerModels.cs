using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

// =====================================================================================
// Müşteri ekranları için küçük view-model record'lar (API ApiClient yanıtlarına eşlenir).
// Bu dosya Müşteri klasöründe — çakışma yok. Yalnız Portal tarafında okunur.
// =====================================================================================

/// <summary>API: /api/v1/admin/licenses satırı (LicenseListItem ile alan-uyumlu).</summary>
public record CustomerLicenseVm(
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
    DateTime? ExpiresUtc)
{
    public LicensePackage Pkg => (LicensePackage)Package;
    public Feature Features => PackageMatrix.Features(Pkg);

    /// <summary>Bitişe kalan gün (süresiz → null).</summary>
    public int? DaysLeft => ExpiresUtc is { } e ? (int)Math.Ceiling((e - DateTime.UtcNow).TotalDays) : null;

    public bool IsPerpetual => ExpiresUtc is null;
}

/// <summary>API: /api/v1/admin/licenses/{id} (License entity) — token indirme için gerekli alanlar.</summary>
public record LicenseFullVm(
    int Id,
    string LicenseCode,
    string? LicenseToken);

/// <summary>API: /api/v1/usage satırı (UsageReportResp ile alan-uyumlu).</summary>
public record UsageVm(
    int Id,
    int? BusinessId,
    string CompanyCode,
    DateTime ReportedUtc,
    int BranchCount,
    int UserCount,
    int KioskCount,
    decimal Revenue,
    string? Period);

/// <summary>API: /api/v1/payments satırı (PaymentResp ile alan-uyumlu).</summary>
public record PaymentVm(
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

/// <summary>API: /api/v1/payments/start yanıtı (StartPaymentResp ile alan-uyumlu).</summary>
public record StartPaymentResultVm(
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

/// <summary>API: /api/v1/package-prices/catalog satırı (PackagePricePublicResp ile alan-uyumlu).</summary>
public record CatalogVm(
    int Package,
    string PackageName,
    DurationKind Duration,
    decimal Price,
    string CurrencyCode,
    decimal MarginPercent,
    decimal? BasePrice);

/// <summary>API: /api/v1/tickets liste satırı (TicketListItem ile alan-uyumlu).</summary>
public record TicketVm(
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

/// <summary>API: /api/v1/tickets/{id} detay (TicketDetailResp ile alan-uyumlu).</summary>
public record TicketDetailVm(
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
    List<TicketMessageVm> Messages);

public record TicketMessageVm(
    int Id,
    string AuthorUserId,
    string Body,
    bool IsInternal,
    DateTime CreatedUtc);

/// <summary>Müşteri ekranlarında ortak görsel yardımcılar (rozet sınıfı, etiket, ikon).</summary>
public static class CustomerUi
{
    public static string LicenseBadge(LicenseStatus s) => s switch
    {
        LicenseStatus.Active => "success",
        LicenseStatus.Trial => "info",
        LicenseStatus.Expired => "warning",
        LicenseStatus.Upgraded => "primary",
        LicenseStatus.Revoked => "danger",
        _ => ""
    };

    public static string LicenseLabel(LicenseStatus s) => s switch
    {
        LicenseStatus.Active => "Aktif",
        LicenseStatus.Trial => "Deneme",
        LicenseStatus.Expired => "Süresi Doldu",
        LicenseStatus.Upgraded => "Yükseltildi",
        LicenseStatus.Revoked => "İptal",
        _ => s.ToString()
    };

    public static string PaymentBadge(PaymentStatus s) => s switch
    {
        PaymentStatus.Paid => "success",
        PaymentStatus.Pending => "warning",
        PaymentStatus.Failed => "danger",
        PaymentStatus.Refunded => "info",
        PaymentStatus.Cancelled => "",
        _ => ""
    };

    public static string PaymentLabel(PaymentStatus s) => s switch
    {
        PaymentStatus.Paid => "Ödendi",
        PaymentStatus.Pending => "Bekliyor",
        PaymentStatus.Failed => "Başarısız",
        PaymentStatus.Refunded => "İade",
        PaymentStatus.Cancelled => "İptal",
        _ => s.ToString()
    };

    public static string ProviderLabel(PaymentProviderKind p) => p switch
    {
        PaymentProviderKind.Iyzico => "iyzico",
        PaymentProviderKind.PayTR => "PayTR",
        PaymentProviderKind.GarantiVPos => "Garanti Sanal POS",
        PaymentProviderKind.BankTransfer => "Banka Havalesi/EFT",
        _ => p.ToString()
    };

    public static string TicketBadge(TicketStatus s) => s switch
    {
        TicketStatus.Open => "info",
        TicketStatus.InProgress => "primary",
        TicketStatus.Answered => "success",
        TicketStatus.Closed => "",
        _ => ""
    };

    public static string TicketLabel(TicketStatus s) => s switch
    {
        TicketStatus.Open => "Açık",
        TicketStatus.InProgress => "İşlemde",
        TicketStatus.Answered => "Yanıtlandı",
        TicketStatus.Closed => "Kapalı",
        _ => s.ToString()
    };

    public static string PriorityBadge(TicketPriority p) => p switch
    {
        TicketPriority.Low => "",
        TicketPriority.Normal => "info",
        TicketPriority.High => "warning",
        TicketPriority.Urgent => "danger",
        _ => ""
    };

    public static string PriorityLabel(TicketPriority p) => p switch
    {
        TicketPriority.Low => "Düşük",
        TicketPriority.Normal => "Normal",
        TicketPriority.High => "Yüksek",
        TicketPriority.Urgent => "Acil",
        _ => p.ToString()
    };

    public static string DurationLabel(DurationKind d) => d switch
    {
        DurationKind.Perpetual => "Süresiz",
        DurationKind.Monthly => "Aylık",
        DurationKind.Yearly => "Yıllık",
        _ => d.ToString()
    };

    /// <summary>Özellik (Feature) → (etiket, lucide ikon adı).</summary>
    public static (string Label, string Icon) FeatureInfo(Feature f) => f switch
    {
        Feature.Adisyon => ("Adisyon / POS", "receipt"),
        Feature.ProductBasic => ("Ürün & Fiyat", "package"),
        Feature.LocationTables => ("Masa & Lokasyon", "layout-grid"),
        Feature.CashXZ => ("Kasa & X/Z Rapor", "calculator"),
        Feature.BasicCustomer => ("Cari (Basit)", "users"),
        Feature.Kitchen => ("Mutfak (KDS)", "chef-hat"),
        Feature.Recipe => ("Reçete / Menü", "book-open"),
        Feature.SelfService => ("KIOSK Self-Servis", "monitor-smartphone"),
        Feature.Reservation => ("Rezervasyon", "calendar-check"),
        Feature.Delivery => ("Paket / Kurye", "bike"),
        Feature.Campaign => ("Kampanya", "tag"),
        Feature.AdvancedReports => ("Detaylı Raporlar", "bar-chart-3"),
        Feature.PreAccounting => ("Ön Muhasebe", "landmark"),
        Feature.AiHub => ("AI Hub", "sparkles"),
        Feature.MultiBranch => ("Çoklu Şube", "building-2"),
        _ => (f.ToString(), "circle")
    };

    /// <summary>Gösterilecek tüm özellikler (None hariç), sabit sırada.</summary>
    public static readonly Feature[] AllFeatures =
    {
        Feature.Adisyon, Feature.ProductBasic, Feature.LocationTables, Feature.CashXZ, Feature.BasicCustomer,
        Feature.Kitchen, Feature.Recipe, Feature.SelfService, Feature.Reservation, Feature.Delivery,
        Feature.Campaign, Feature.AdvancedReports, Feature.PreAccounting, Feature.AiHub, Feature.MultiBranch
    };

    public static string Money(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
}
