namespace MindAeonRest.Services;

/// <summary>Gelen (inbound) teslimat kalemi ↔ yerel ürün eşleme GÜVENİ.
/// Yalnız <see cref="Verified"/> platform SKU haritasından gelir (güvenilir); Code/Name yedekleri
/// FARKLI AD-UZAYLARI olduğundan <see cref="AutoUnverified"/> işaretlenir (sessiz kabul edilmez, kontrol gerekir).</summary>
public enum InboundMatchConfidence
{
    /// <summary>DeliveryProductMap üzerinden doğrulanmış eşleme (platform SKU → yerel ürün).</summary>
    Verified = 0,
    /// <summary>Code/Name yedeği ile bulundu ama haritada yok → DOĞRULANMAMIŞ (yanlış ürün riski görünür kılınır).</summary>
    AutoUnverified = 1,
    /// <summary>Hiç eşleşmedi.</summary>
    Unmatched = 2,
    /// <summary>Birden çok yerel ürün aynı ada sahip → BELİRSİZ; ada göre eşleştirme yapılmaz.</summary>
    Ambiguous = 3
}

/// <summary>Tek kalemin eşleme sonucu.</summary>
public sealed class InboundMatchResult
{
    public int ProductId { get; init; }
    public InboundMatchConfidence Confidence { get; init; }
    public bool IsMatched => Confidence is InboundMatchConfidence.Verified or InboundMatchConfidence.AutoUnverified;
}

/// <summary>Teslimat gelen-sipariş eşleme + fiyat/vergi çözümünün SAF (DB'siz) mantığı — birim testlenebilir.
/// DeliveryInboundService bu mantığı yerel katalogla besleyerek kullanır.</summary>
public static class DeliveryInboundMatch
{
    /// <summary>Bir gelen kalemi güven katmanlı çözer:
    /// 1) <paramref name="verifiedMap"/> (DeliveryProductMap, platform SKU) = DOĞRULANMIŞ;
    /// 2) yerel Products.Code yedeği = DOĞRULANMAMIŞ (farklı ad-uzayı);
    /// 3) yerel Name yedeği = DOĞRULANMAMIŞ, çift ad ise BELİRSİZ (eşleştirilmez).
    /// <paramref name="productExists"/> stale/silinmiş id'leri eler (o durumda bir sonraki yedeğe düşer).</summary>
    public static InboundMatchResult MatchItem(
        string? externalCode,
        string? name,
        IReadOnlyDictionary<string, int> verifiedMap,
        IReadOnlyDictionary<string, int> byCode,
        IReadOnlyDictionary<string, int> byName,
        IReadOnlySet<string> ambiguousNames,
        Func<int, bool> productExists)
    {
        // 1) DOĞRULANMIŞ: platform SKU haritası (birincil, güvenilir kaynak).
        if (!string.IsNullOrWhiteSpace(externalCode)
            && verifiedMap.TryGetValue(externalCode!, out int vid) && productExists(vid))
            return new InboundMatchResult { ProductId = vid, Confidence = InboundMatchConfidence.Verified };

        // 2) DOĞRULANMAMIŞ yedek: platform kodunu yerel Products.Code ile deneme (farklı ad-uzayı → riskli).
        if (!string.IsNullOrWhiteSpace(externalCode)
            && byCode.TryGetValue(externalCode!, out int cid) && productExists(cid))
            return new InboundMatchResult { ProductId = cid, Confidence = InboundMatchConfidence.AutoUnverified };

        // 3) DOĞRULANMAMIŞ yedek: ada göre. Çift ad → BELİRSİZ (deterministik değil → eşleştirme).
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (ambiguousNames.Contains(name!))
                return new InboundMatchResult { Confidence = InboundMatchConfidence.Ambiguous };
            if (byName.TryGetValue(name!, out int nid) && productExists(nid))
                return new InboundMatchResult { ProductId = nid, Confidence = InboundMatchConfidence.AutoUnverified };
        }

        return new InboundMatchResult { Confidence = InboundMatchConfidence.Unmatched };
    }

    /// <summary>Sentinel yok: <paramref name="inbound"/> null ise BELİRTİLMEMİŞ → katalog değeri;
    /// null değilse (0 dahil) platformun bildirdiği GEÇERLİ değer kullanılır (ör. promosyon 0 fiyat EZİLMEZ).</summary>
    public static decimal Resolve(decimal? inbound, decimal catalog) => inbound ?? catalog;
}
