using System.ComponentModel.DataAnnotations;

namespace MindAeonRest.Licence.Data;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }

    // Soft-delete + denetim izi (Foundation)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }

    /// <summary>Eşzamanlılık denetimi (optimistic concurrency).</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}

/// <summary>Bayi — kar marjı + tahsilat modeli bayi bazında.</summary>
public class Dealer : BaseEntity
{
    [MaxLength(40)] public string Code { get; set; } = "";     // bayiye verdiğimiz kod — lisans atfı bununla yapılır
    [MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(150)] public string? ContactName { get; set; }
    [MaxLength(150)] public string? Email { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    public decimal MarginPercent { get; set; }                 // bayi kar marjı %
    public BillingMode BillingMode { get; set; } = BillingMode.CustomerPaysUs;
    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Notes { get; set; }

    public List<Business> Businesses { get; set; } = new();
    public List<License> Licenses { get; set; } = new();
}

/// <summary>Ticari işletme (lisans sahibi) — bayi tarafından kurulur, bilgisi kaydedilir.</summary>
public class Business : BaseEntity
{
    public int? DealerId { get; set; }
    public Dealer? Dealer { get; set; }
    [MaxLength(50)] public string CompanyCode { get; set; } = "";   // restoran sistemindeki şirket kodu
    [MaxLength(200)] public string Title { get; set; } = "";        // unvan
    [MaxLength(120)] public string? TaxOffice { get; set; }
    [MaxLength(40)] public string? TaxNumber { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    [MaxLength(80)] public string? City { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(150)] public string? Email { get; set; }
    [MaxLength(100)] public string? HardwareId { get; set; }        // lisans donanım bağı

    public List<License> Licenses { get; set; } = new();
}

/// <summary>Add-on fiyat türü: modül / kullanıcı başı / terminal başı / şube başı.</summary>
public enum AddOnKind { Module = 0, User = 1, Kiosk = 2, Branch = 3 }

/// <summary>Add-on fiyat kalemi: satılan modül veya birim (kullanıcı/terminal/şube) fiyatı.</summary>
public class AddOnPrice : BaseEntity
{
    public AddOnKind Kind { get; set; }
    public long FeatureBit { get; set; }                            // Kind=Module ise hangi Feature; diğerlerinde 0
    [MaxLength(100)] public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }                          // modül için tek fiyat; birim kindlerinde adet başı
    [MaxLength(8)] public string CurrencyCode { get; set; } = "TRY";
    public bool IsActive { get; set; } = true;
}

/// <summary>Paket taban fiyatı (paket + süre). Marj bayi üzerinden hesaplanır.</summary>
public class PackagePrice : BaseEntity
{
    public int Package { get; set; }                               // MindAeonRest.Licensing.LicensePackage (int)
    public DurationKind Duration { get; set; }
    public decimal BasePrice { get; set; }
    [MaxLength(8)] public string CurrencyCode { get; set; } = "TRY";
    public bool IsActive { get; set; } = true;
}

/// <summary>Üretilmiş lisans (imzalı token) — işletme + bayi + paket + durum + geçmiş.</summary>
public class License : BaseEntity
{
    public int BusinessId { get; set; }
    public Business? Business { get; set; }
    public int? DealerId { get; set; }
    public Dealer? Dealer { get; set; }

    [MaxLength(40)] public string LicenseCode { get; set; } = "";   // kısa kimlik
    public int Package { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public int MaxKiosks { get; set; } = 1;          // toplam terminal limiti (paket + ekstra)
    public long FeatureBits { get; set; }            // nihai özellik seti (paket çekirdeği | add-on'lar)
    public long AddOnFeatures { get; set; }          // yalnız satılan add-on modüller (raporlama/yenileme)
    public int ExtraUsers { get; set; }              // pakete eklenen ekstra kullanıcı
    public int ExtraKiosks { get; set; }             // pakete eklenen ekstra terminal
    public int ExtraBranches { get; set; }           // pakete eklenen ekstra şube
    [MaxLength(100)] public string HardwareId { get; set; } = "";
    public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public LicenseStatus Status { get; set; } = LicenseStatus.Active;
    public string LicenseToken { get; set; } = "";                 // imzalı MAR-LIC token
    [MaxLength(500)] public string? Notes { get; set; }
    public string? IssuedByUserId { get; set; }

    public List<LicenseHistory> History { get; set; } = new();
}

public class LicenseHistory : BaseEntity
{
    public int LicenseId { get; set; }
    public License? License { get; set; }
    public LicenseAction Action { get; set; }
    public int? FromPackage { get; set; }
    public int? ToPackage { get; set; }
    public string? ByUserId { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

/// <summary>Müşteri/masaüstünden gelen lisans talebi (talep anahtarı) — onay/üretim bekler.</summary>
public class LicenseRequest : BaseEntity
{
    public int? BusinessId { get; set; }
    public int? DealerId { get; set; }
    [MaxLength(50)] public string CompanyCode { get; set; } = "";
    [MaxLength(100)] public string HardwareId { get; set; } = "";
    public int RequestedPackage { get; set; }
    public string RequestToken { get; set; } = "";
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int? IssuedLicenseId { get; set; }
}

/// <summary>Demo kurulumu kaydı (kim, ne zaman, hangi işletme).</summary>
public class DemoInstall : BaseEntity
{
    public int? DealerId { get; set; }
    [MaxLength(50)] public string CompanyCode { get; set; } = "";
    [MaxLength(200)] public string? BusinessTitle { get; set; }
    [MaxLength(100)] public string? HardwareId { get; set; }
    [MaxLength(150)] public string? ContactName { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(150)] public string? Email { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public bool Converted { get; set; }
    public int? LicenseId { get; set; }
}

/// <summary>Ödeme/işlem — sağlayıcı + marj + net; bayi bazlı tahsilat modeli.</summary>
public class Payment : BaseEntity
{
    public int? DealerId { get; set; }
    public int? BusinessId { get; set; }
    public int? LicenseId { get; set; }
    public PaymentProviderKind Provider { get; set; }
    public BillingMode BillingMode { get; set; }
    public decimal Amount { get; set; }                            // brüt
    public decimal MarginAmount { get; set; }                      // bayi payı
    public decimal NetAmount { get; set; }                         // bizim net
    [MaxLength(8)] public string CurrencyCode { get; set; } = "TRY";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    [MaxLength(200)] public string? ProviderRef { get; set; }      // sağlayıcı işlem no
    public DateTime? PaidUtc { get; set; }
}

/// <summary>Ödeme sağlayıcı yapılandırması (şifreli config); DealerId null=global.</summary>
public class PaymentProviderConfig : BaseEntity
{
    public PaymentProviderKind Provider { get; set; }
    public int? DealerId { get; set; }
    public bool IsActive { get; set; }
    public string ConfigJsonEncrypted { get; set; } = "";          // api key/secret vb. (şifreli)
}

/// <summary>Destek talebi — bayi veya müşteri açar, bize gelir.</summary>
public class Ticket : BaseEntity
{
    public string OpenerUserId { get; set; } = "";
    public int? DealerId { get; set; }
    public int? BusinessId { get; set; }
    [MaxLength(200)] public string Subject { get; set; } = "";
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public string? AssignedToUserId { get; set; }
    public DateTime? ClosedUtc { get; set; }

    public List<TicketMessage> Messages { get; set; } = new();
}

public class TicketMessage : BaseEntity
{
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public string AuthorUserId { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsInternal { get; set; }                           // sadece bizim ekip görür
}

/// <summary>Masaüstünden periyodik kullanım/ciro raporu (paket denetimi + ciro takibi).</summary>
public class UsageReport : BaseEntity
{
    public int? BusinessId { get; set; }
    [MaxLength(50)] public string CompanyCode { get; set; } = "";
    public DateTime ReportedUtc { get; set; } = DateTime.UtcNow;
    public int BranchCount { get; set; }
    public int UserCount { get; set; }
    public int KioskCount { get; set; }
    public decimal Revenue { get; set; }                           // dönem cirosu
    [MaxLength(20)] public string? Period { get; set; }            // "2026-06" gibi
}
