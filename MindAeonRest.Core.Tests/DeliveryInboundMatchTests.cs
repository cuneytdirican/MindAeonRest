using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// Teslimat GELEN sipariş eşleme + fiyat/vergi sentinel mantığının SAF (DB'siz) testleri.
/// DeliveryInboundMatch, MindAeonRest.Core'daki gerçek üretim koduyla aynıdır (ProjectReference).
/// BULGU 10 (güven katmanlı eşleme) + BULGU 14 (nullable fiyat/vergi sentinel'i) kapsanır.
/// </summary>
public class DeliveryInboundMatchTests
{
    private static readonly Dictionary<string, int> Empty = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NoAmbig = new(StringComparer.OrdinalIgnoreCase);
    private static bool Exists(int _) => true;

    private static Dictionary<string, int> Map(params (string k, int v)[] kv)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    // ===== BULGU 10: güven katmanlı eşleme =====

    [Fact]
    public void VerifiedMap_IsPrimary_AndWinsOverCodeAndName()
    {
        // DeliveryProductMap SKU-1 → 100; yerel Code "SKU-1" → 200 olsa bile harita KAZANIR, DOĞRULANMIŞ.
        var r = DeliveryInboundMatch.MatchItem("SKU-1", "Kola",
            verifiedMap: Map(("SKU-1", 100)), byCode: Map(("SKU-1", 200)), byName: Map(("Kola", 300)),
            NoAmbig, Exists);
        Assert.Equal(100, r.ProductId);
        Assert.Equal(InboundMatchConfidence.Verified, r.Confidence);
        Assert.True(r.IsMatched);
    }

    [Fact]
    public void CodeFallback_IsAutoUnverified_NotSilentlyTrusted()
    {
        // Haritada yok → yerel Code ile bulundu: eşleşir AMA DOĞRULANMAMIŞ (farklı ad-uzayı, kontrol gerekir).
        var r = DeliveryInboundMatch.MatchItem("BURGER", null,
            verifiedMap: Empty, byCode: Map(("BURGER", 42)), byName: Empty, NoAmbig, Exists);
        Assert.Equal(42, r.ProductId);
        Assert.Equal(InboundMatchConfidence.AutoUnverified, r.Confidence);
    }

    [Fact]
    public void NameFallback_IsAutoUnverified()
    {
        var r = DeliveryInboundMatch.MatchItem("UNKNOWN-SKU", "Ayran",
            verifiedMap: Empty, byCode: Empty, byName: Map(("Ayran", 7)), NoAmbig, Exists);
        Assert.Equal(7, r.ProductId);
        Assert.Equal(InboundMatchConfidence.AutoUnverified, r.Confidence);
    }

    [Fact]
    public void DuplicateLocalName_IsAmbiguous_NotMatched()
    {
        // "Tost" adına iki yerel ürün → belirsiz; ada göre eşleştirilmez (yanlış ürün riskini önle).
        var ambig = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Tost" };
        var r = DeliveryInboundMatch.MatchItem("X", "Tost",
            verifiedMap: Empty, byCode: Empty, byName: Map(("Tost", 9)), ambig, Exists);
        Assert.Equal(InboundMatchConfidence.Ambiguous, r.Confidence);
        Assert.False(r.IsMatched);
    }

    [Fact]
    public void NoMatchAnywhere_IsUnmatched()
    {
        var r = DeliveryInboundMatch.MatchItem("ZZZ", "Yok Böyle Ürün",
            verifiedMap: Empty, byCode: Empty, byName: Empty, NoAmbig, Exists);
        Assert.Equal(InboundMatchConfidence.Unmatched, r.Confidence);
        Assert.False(r.IsMatched);
    }

    [Fact]
    public void StaleVerifiedId_FallsThroughToNextSource()
    {
        // Harita SKU-1 → 999 (silinmiş/stale); productExists false → bir sonraki yedeğe (Code) düşer.
        var r = DeliveryInboundMatch.MatchItem("SKU-1", null,
            verifiedMap: Map(("SKU-1", 999)), byCode: Map(("SKU-1", 50)), byName: Empty,
            NoAmbig, productExists: id => id != 999);
        Assert.Equal(50, r.ProductId);
        Assert.Equal(InboundMatchConfidence.AutoUnverified, r.Confidence);
    }

    [Fact]
    public void MissingExternalCode_UsesNameOnly()
    {
        var r = DeliveryInboundMatch.MatchItem(externalCode: null, name: "Su",
            verifiedMap: Map(("Su", 1)), byCode: Map(("Su", 2)), byName: Map(("Su", 3)), NoAmbig, Exists);
        // externalCode yok → map/code atlanır, yalnız ada göre (doğrulanmamış).
        Assert.Equal(3, r.ProductId);
        Assert.Equal(InboundMatchConfidence.AutoUnverified, r.Confidence);
    }

    // ===== BULGU 14: fiyat/vergi sentinel'i (nullable) =====

    [Fact]
    public void Resolve_Null_UsesCatalog()
    {
        // Platform fiyat BELİRTMEDİ (null) → katalog fiyatı.
        Assert.Equal(25m, DeliveryInboundMatch.Resolve(null, 25m));
    }

    [Fact]
    public void Resolve_Zero_IsValidPromoPrice_NotOverwritten()
    {
        // Platform GEÇERLİ 0 ₺ bildirdi (promosyon) → katalog fiyatı EZMEZ; 0 kullanılır.
        Assert.Equal(0m, DeliveryInboundMatch.Resolve(0m, 25m));
    }

    [Fact]
    public void Resolve_PositiveValue_UsesPlatformValue()
    {
        Assert.Equal(18.5m, DeliveryInboundMatch.Resolve(18.5m, 25m));
    }
}
