using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MindAeonRest.Licensing;

namespace MindAeonRestWeb.Services;

/// <summary>Tanıtım sitesi fiyat/paket verisini Licence.Api'nin anonim public ucundan çeker (cache'li).
/// Ağ/uç yoksa PackageMatrix'ten STATİK fallback (özellikler + "Fiyat için iletişim") üretir → site hep çalışır.</summary>
public class LicenceCatalogClient(HttpClient http, IMemoryCache cache, ILogger<LicenceCatalogClient> log)
{
    private const string CacheKey = "public-pricing";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public sealed record PricingView(List<PackageCard> Packages, List<AddOnCard> AddOns, string Currency, bool Live);
    public sealed record PackageCard(string Code, string Name, decimal? Monthly, decimal? Yearly, string Currency,
        int MaxUsers, int MaxKiosks, int MaxBranches, List<string> Features, bool Highlighted);
    public sealed record AddOnCard(string Name, string Kind, decimal UnitPrice, string Currency);

    public async Task<PricingView> GetAsync()
    {
        if (cache.TryGetValue<PricingView>(CacheKey, out var cached) && cached is not null) return cached;

        PricingView view;
        try
        {
            using var res = await http.GetAsync("/api/v1/public/pricing");
            res.EnsureSuccessStatusCode();
            var dto = JsonSerializer.Deserialize<PublicPricingDto>(await res.Content.ReadAsStringAsync(), Json)
                      ?? throw new InvalidOperationException("boş yanıt");
            view = MapLive(dto);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Public pricing alınamadı; statik fallback kullanılıyor.");
            view = Fallback();
        }

        cache.Set(CacheKey, view, Ttl);
        return view;
    }

    private static PricingView MapLive(PublicPricingDto dto)
    {
        var cur = string.IsNullOrWhiteSpace(dto.Currency) ? "TRY" : dto.Currency!;
        var pkgs = (dto.Packages ?? new()).Select(p => new PackageCard(
            p.Code ?? "", p.Name ?? p.Code ?? "", p.Monthly, p.Yearly, string.IsNullOrWhiteSpace(p.Currency) ? cur : p.Currency!,
            p.MaxUsers, p.MaxKiosks, p.MaxBranches, p.Features ?? new(),
            (p.Code ?? "").Equals("Standart", StringComparison.OrdinalIgnoreCase))).ToList();
        var addons = (dto.AddOns ?? new()).Select(a => new AddOnCard(
            a.Name ?? "", a.Kind ?? "Module", a.UnitPrice, string.IsNullOrWhiteSpace(a.Currency) ? cur : a.Currency!)).ToList();
        if (pkgs.Count == 0) return Fallback();
        return new PricingView(pkgs, addons, cur, Live: true);
    }

    /// <summary>Licence.Api erişilemezse: paketler + özellikler PackageMatrix'ten; fiyat null → "İletişim".</summary>
    private static PricingView Fallback()
    {
        var packages = new List<PackageCard>();
        foreach (var pkg in new[] { LicensePackage.Lite, LicensePackage.Standart, LicensePackage.Enterprise })
        {
            packages.Add(new PackageCard(
                pkg.ToString(), PackageDisplayName(pkg), null, null, "TRY",
                PackageMatrix.MaxUsers(pkg), PackageMatrix.MaxKiosks(pkg), PackageMatrix.MaxBranches(pkg),
                FeatureLabels(PackageMatrix.Features(pkg)), pkg == LicensePackage.Standart));
        }
        return new PricingView(packages, new(), "TRY", Live: false);
    }

    public static string PackageDisplayName(LicensePackage p) => p switch
    {
        LicensePackage.Lite => "Lite",
        LicensePackage.Standart => "Standart",
        LicensePackage.Enterprise => "Enterprise",
        _ => p.ToString()
    };

    // Fallback pricing kartlarında paket başına gösterilen modül etiketleri. Paket üyeliği PackageMatrix (Feature bit'leri)
    // ile belirlenir; buradaki etiketler yeni modülleri ilgili özelliğin altında pazarlama diliyle yüzeye çıkarır (additive).
    private static readonly (Feature Bit, string Label)[] FeatureMap =
    {
        (Feature.Adisyon, "Adisyon / POS / Ödeme · Offline çalışır"),
        (Feature.Adisyon, "QR ile masadan sipariş"),
        (Feature.ProductBasic, "Ürün & Fiyat"),
        (Feature.LocationTables, "Lokasyon & Masa Planı · Masa QR Kodları"),
        (Feature.CashXZ, "Kasa X/Z Raporu · Vardiya"),
        (Feature.BasicCustomer, "Cari / Müşteri · Sadakat & Geri Bildirim"),
        (Feature.Kitchen, "Mutfak Ekranı (KDS)"),
        (Feature.Recipe, "Reçete / Menü"),
        (Feature.SelfService, "Self-Servis KIOSK · Müşteri Sadakat Uygulaması"),
        (Feature.Reservation, "Online Rezervasyon & Bekleme Listesi"),
        (Feature.Delivery, "Paket / Kurye · Trendyol · Getir · Yemeksepeti"),
        (Feature.Campaign, "Kampanyalar · SMS / WhatsApp Gönderim"),
        (Feature.AdvancedReports, "Gelişmiş Raporlar · Menü Analizi · Stok Zekâsı"),
        (Feature.PreAccounting, "Ön Muhasebe / Evrak · e-Fatura · Personel/Puantaj"),
        (Feature.AiHub, "AI Hub"),
        (Feature.MultiBranch, "Çoklu Şube"),
    };

    public static List<string> FeatureLabels(Feature f) =>
        FeatureMap.Where(m => f.HasFlag(m.Bit)).Select(m => m.Label).ToList();

    // Licence.Api /public/pricing yanıtı (gevşek eşleşme)
    private sealed class PublicPricingDto
    {
        public string? Currency { get; set; }
        public List<PkgDto>? Packages { get; set; }
        public List<AddonDto>? AddOns { get; set; }
    }
    private sealed class PkgDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public decimal? Monthly { get; set; }
        public decimal? Yearly { get; set; }
        public string? Currency { get; set; }
        public int MaxUsers { get; set; }
        public int MaxKiosks { get; set; }
        public int MaxBranches { get; set; }
        public List<string>? Features { get; set; }
    }
    private sealed class AddonDto
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Currency { get; set; }
    }
}
