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
