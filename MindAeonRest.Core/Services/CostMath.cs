namespace MindAeonRest.Services;

/// <summary>
/// FAZ 1a Maliyet Motoru — saf (DB'siz) maliyet hesap mantığı. Birim test edilebilir olması için
/// ağırlıklı-ortalama ve reçete rollup formülleri buraya izole edildi (CostService bunları çağırır).
/// </summary>
public static class CostMath
{
    /// <summary>
    /// Ağırlıklı-ortalama alış maliyeti: yeniAvg = (mevcutStokDeğeri + alınanMiktar×alışBirimFiyat) / (mevcutStok + alınanMiktar).
    /// Kurallar: geçersiz/0 giriş miktarında maliyet değişmez; mevcut stok yok/negatif ise yeni maliyet doğrudan giriş fiyatıdır
    /// (negatif stok değeri ortalamayı bozmasın). Sonuç 4 haneye yuvarlanır.
    /// </summary>
    public static decimal WeightedAvg(decimal oldQty, decimal oldAvg, decimal inQty, decimal inPrice)
    {
        if (inQty <= 0) return oldAvg;        // giriş yok/geçersiz → maliyet aynen korunur
        if (oldQty <= 0) return decimal.Round(inPrice, 4);   // mevcut stok yok/negatif → maliyet = giriş fiyatı
        decimal newQty = oldQty + inQty;
        decimal value = oldQty * oldAvg + inQty * inPrice;
        return decimal.Round(value / newQty, 4);
    }

    /// <summary>
    /// BULGU 1 — İrsaliye→Fatura maliyet düzeltmesi: alış faturasının NİHAİ satır tutarı, kaynak (fiyatlı)
    /// irsaliyeninkinden farklıysa aradaki değer farkını (valueDelta = faturaNet − irsaliyeNet) mevcut stok
    /// bakiyesine dağıtarak AvgCost'u düzeltir. Fark 0 ise (fiyat aynı) maliyet değişmez → mevcut doğru davranış korunur.
    /// Bakiye yok/negatifse dağıtım yapılmaz (bir sonraki alış zaten düzeltir). Sonuç 4 haneye yuvarlanır.
    /// </summary>
    public static decimal PriceCorrectedAvg(decimal currentAvg, decimal currentBalance, decimal valueDelta)
    {
        if (valueDelta == 0m) return currentAvg;      // fatura fiyatı = irsaliye fiyatı → değişiklik yok
        if (currentBalance <= 0m) return currentAvg;  // dağıtılacak stok yok → dokunma
        return decimal.Round(currentAvg + valueDelta / currentBalance, 4);
    }

    /// <summary>
    /// BULGU 8 — İptal sonrası yeniden hesap: bir ürünün kalan (fiyatlı) alışlarının ağırlıklı ortalaması
    /// = Σ(net tutar) / Σ(miktar). Fiyatlı alış geçmişi kalmadıysa (kalanMiktar ≤ 0) mevcut maliyet korunur
    /// (bir sonraki alış CostMath.WeightedAvg ile zaten düzeltir). Sonuç 4 haneye yuvarlanır.
    /// </summary>
    public static decimal RecomputeAvg(decimal remainingNetValue, decimal remainingQty, decimal fallbackAvg)
    {
        if (remainingQty <= 0m) return fallbackAvg;
        return decimal.Round(remainingNetValue / remainingQty, 4);
    }

    /// <summary>
    /// Reçete (menü) maliyet rollup'ı: Σ(bileşenMaliyeti × Quantity). Bileşen maliyetleri çağıran tarafça
    /// (özyinelemeli olarak) çözülür; bu metot yalnız toplamı alır. Sonuç 4 haneye yuvarlanır.
    /// </summary>
    public static decimal RollupCost(IEnumerable<(decimal Quantity, decimal ComponentCost)> components)
        => decimal.Round(components.Sum(x => x.Quantity * x.ComponentCost), 4);

    /// <summary>Marj tutarı = satışFiyatı − maliyet.</summary>
    public static decimal MarginAmount(decimal cost, decimal salePrice) => salePrice - cost;

    /// <summary>Marj yüzdesi (satış fiyatı üzerinden katkı marjı): (satış−maliyet)/satış×100. Satış 0 ise 0.</summary>
    public static decimal MarginPercent(decimal cost, decimal salePrice)
        => salePrice == 0 ? 0m : decimal.Round((salePrice - cost) / salePrice * 100m, 2);
}
