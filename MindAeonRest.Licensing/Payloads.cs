namespace MindAeonRest.Licensing;

/// <summary>Müşteri tarafının ürettiği TALEP (request) — bayi bunu bize iletir.</summary>
public class RequestPayload
{
    public string CompanyCode { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public LicensePackage RequestedPackage { get; set; } = LicensePackage.Lite;
    public string? DealerCode { get; set; }   // bayi kodu (opsiyonel) — portal lisansı bu bayiye atfeder
    public string? AppVersion { get; set; }
    public System.DateTime CreatedUtc { get; set; } = System.DateTime.UtcNow;
}

/// <summary>Bizim ürettiğimiz LİSANS (imzalanır) — müşteri uygulaması doğrular.</summary>
public class LicensePayload
{
    public string LicenseId { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public string? DealerCode { get; set; }   // lisansı üreten bayinin kodu (izlenebilirlik)
    public LicensePackage Package { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public int MaxKiosks { get; set; } = 1;   // izin verilen terminal (KIOSK) sayısı
    public long FeatureBits { get; set; }
    public System.DateTime IssuedUtc { get; set; } = System.DateTime.UtcNow;
    public System.DateTime? ExpiresUtc { get; set; }   // null = süresiz

    public Feature Features => (Feature)FeatureBits;
    public bool IsExpired => ExpiresUtc is { } e && System.DateTime.UtcNow > e;
}

/// <summary>
/// MERKEZÎ (web/SaaS) lisans sabitleri. Merkezî web host'taki tenant'ların gerçek bir donanımı YOKTUR
/// (tek cloud host tüm kiracılarla paylaşılır). Portal "Lisans Ata" akışı bu yüzden lisansı sabit
/// <see cref="HardwareId"/> ("WEB-CENTRAL") için imzalar; POS tarafı (GetStatusByCompany) donanım bağı
/// aramaz, yalnız CompanyCode ile çözer. Masaüstü donanım kimlikleri "XXXXX-XXXXX-XXXXX-XXXXX" HEX
/// biçimindedir → "WEB" önekiyle ASLA çakışmaz; bu sayede merkezî satırlar güvenle ayırt edilir.
/// </summary>
public static class CentralLicense
{
    /// <summary>Merkezî web tenant lisanslarının sabit donanım kimliği (Licence API + POS aynı değeri kullanır).</summary>
    public const string HardwareId = "WEB-CENTRAL";

    /// <summary>Merkezî/web satır öneki. Eski kayıtlar: "WEB" (web lisans sayfası boş-donanım yedeği),
    /// "WEB-ENTERPRISE" (tools/MakeDemoLicense).</summary>
    public const string Prefix = "WEB";

    /// <summary>Donanım kimliği merkezî/web satırı mı? (masaüstü terminal kimliği değil)</summary>
    public static bool IsCentral(string? hardwareId) =>
        !string.IsNullOrWhiteSpace(hardwareId) && hardwareId.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase);
}
