namespace MindAeonRest.Licensing;

/// <summary>Lisans paketleri.</summary>
public enum LicensePackage { Lite = 0, Standart = 1, Enterprise = 2 }

/// <summary>Pakete göre açık/kapalı modüller (bit maske).</summary>
[System.Flags]
public enum Feature : long
{
    None = 0,
    Adisyon = 1 << 0,          // POS / Masa / Ödeme — çekirdek
    ProductBasic = 1 << 1,     // Ürün / Kategori / Fiyat (basit)
    LocationTables = 1 << 2,   // Lokasyon / Reyon / Masa planı
    CashXZ = 1 << 3,           // Kasa + X/Z rapor
    BasicCustomer = 1 << 4,    // Basit cari (açık hesap müşterisi)
    Kitchen = 1 << 5,          // Mutfak (KDS)
    Recipe = 1 << 6,           // Reçete / Menü
    SelfService = 1 << 7,      // KIOSK self-servis
    Reservation = 1 << 8,      // Rezervasyon
    Delivery = 1 << 9,         // Paket / Kurye
    Campaign = 1 << 10,        // Kampanya
    AdvancedReports = 1 << 11, // Detaylı raporlar (Fiş/Masa/Dashboard/Ekstre)
    PreAccounting = 1 << 12,   // Ön muhasebe (Cari/Evrak/Fatura/Banka/Çek/Gider)
    AiHub = 1 << 13,           // AI Hub
    MultiBranch = 1 << 14      // Çoklu şube
}

/// <summary>Paket → özellik/limit matrisi. Tek doğruluk kaynağı (hem üretici hem müşteri kullanır).</summary>
public static class PackageMatrix
{
    private const Feature LiteSet =
        Feature.Adisyon | Feature.ProductBasic | Feature.LocationTables | Feature.CashXZ | Feature.BasicCustomer;

    private const Feature StandartSet =
        LiteSet | Feature.Kitchen | Feature.Recipe | Feature.SelfService |
        Feature.Reservation | Feature.Delivery | Feature.Campaign | Feature.AdvancedReports;

    private const Feature EnterpriseSet =
        StandartSet | Feature.PreAccounting | Feature.AiHub | Feature.MultiBranch;

    public static Feature Features(LicensePackage p) => p switch
    {
        LicensePackage.Lite => LiteSet,
        LicensePackage.Standart => StandartSet,
        _ => EnterpriseSet
    };

    /// <summary>Çekirdek (her pakette daima açık) modüller — add-on olarak satılmaz.</summary>
    public const Feature CoreSet = LiteSet;

    /// <summary>Ayrı satılabilen add-on modüller (çekirdek hariç tüm özellikler).</summary>
    public static readonly Feature[] AddOnModules =
    {
        Feature.Kitchen, Feature.Recipe, Feature.SelfService, Feature.Reservation,
        Feature.Delivery, Feature.Campaign, Feature.AdvancedReports,
        Feature.PreAccounting, Feature.AiHub, Feature.MultiBranch
    };

    /// <summary>Modül (Feature) Türkçe adı — add-on listesi/ekranlar için.</summary>
    public static string FeatureName(Feature f) => f switch
    {
        Feature.Adisyon => "Adisyon (POS)",
        Feature.ProductBasic => "Ürün / Fiyat",
        Feature.LocationTables => "Lokasyon / Masa",
        Feature.CashXZ => "Kasa + X/Z",
        Feature.BasicCustomer => "Basit Cari",
        Feature.Kitchen => "Mutfak (KDS)",
        Feature.Recipe => "Reçete / Menü",
        Feature.SelfService => "KIOSK Self-Servis",
        Feature.Reservation => "Rezervasyon",
        Feature.Delivery => "Paket / Kurye",
        Feature.Campaign => "Kampanya",
        Feature.AdvancedReports => "Detaylı Raporlar",
        Feature.PreAccounting => "Ön Muhasebe",
        Feature.AiHub => "AI Hub",
        Feature.MultiBranch => "Çoklu Şube",
        _ => f.ToString()
    };

    /// <summary>Toplam kullanıcı limiti = paket tabanı + ekstra (taban sınırsızsa sınırsız kalır).</summary>
    public static int UsersWith(LicensePackage p, int extra)
    {
        int b = MaxUsers(p);
        return b == int.MaxValue ? int.MaxValue : b + System.Math.Max(0, extra);
    }
    /// <summary>Toplam terminal limiti = paket tabanı + ekstra.</summary>
    public static int KiosksWith(LicensePackage p, int extra)
    {
        int b = MaxKiosks(p);
        return b == int.MaxValue ? int.MaxValue : b + System.Math.Max(0, extra);
    }
    /// <summary>Toplam şube limiti = paket tabanı + ekstra (MultiBranch add-on'u ayrıca sınırsız yapar).</summary>
    public static int BranchesWith(LicensePackage p, int extra, bool multiBranchAddOn)
    {
        if (multiBranchAddOn) return int.MaxValue;
        int b = MaxBranches(p);
        return b == int.MaxValue ? int.MaxValue : b + System.Math.Max(0, extra);
    }

    public static int MaxBranches(LicensePackage p) => p == LicensePackage.Enterprise ? int.MaxValue : 1;
    public static int MaxUsers(LicensePackage p) => p switch { LicensePackage.Lite => 3, LicensePackage.Standart => 7, _ => int.MaxValue };
    /// <summary>İzin verilen terminal (KIOSK) sayısı: Lite 1, Standart 3, Enterprise sınırsız.</summary>
    public static int MaxKiosks(LicensePackage p) => p switch { LicensePackage.Lite => 1, LicensePackage.Standart => 3, _ => int.MaxValue };

    public static string Name(LicensePackage p) => p switch
    {
        LicensePackage.Lite => "Lite",
        LicensePackage.Standart => "Standart",
        _ => "Enterprise"
    };

    public static string Describe(LicensePackage p)
    {
        int b = MaxBranches(p), u = MaxUsers(p), k = MaxKiosks(p);
        string bs = b == int.MaxValue ? "sınırsız şube" : $"{b} şube";
        string us = u == int.MaxValue ? "sınırsız kullanıcı" : $"≤{u} kullanıcı";
        string ks = k == int.MaxValue ? "sınırsız terminal" : $"{k} terminal";
        return $"{Name(p)} · {bs} · {us} · {ks}";
    }
}
