using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 1a Maliyet Motoru — saf (DB'siz) hesap mantığı testleri: ağırlıklı-ortalama alış maliyeti +
/// reçete rollup + marj. CostMath, MindAeonRest.Core'daki gerçek üretim koduyla aynıdır (ProjectReference).
/// </summary>
public class CostMathTests
{
    [Fact]
    public void WeightedAvg_BlendsExistingStockValueWithIncomingBatch()
    {
        // 10 adet @ 20 mevcut (değer 200) + 10 adet @ 30 giriş (değer 300) = 500 / 20 = 25
        decimal avg = CostMath.WeightedAvg(oldQty: 10m, oldAvg: 20m, inQty: 10m, inPrice: 30m);
        Assert.Equal(25m, avg);
    }

    [Fact]
    public void WeightedAvg_UnequalQuantities_WeightsByQty()
    {
        // 30 @ 10 (300) + 10 @ 20 (200) = 500 / 40 = 12.5
        Assert.Equal(12.5m, CostMath.WeightedAvg(30m, 10m, 10m, 20m));
    }

    [Fact]
    public void WeightedAvg_NoExistingStock_TakesIncomingPrice()
    {
        // mevcut stok 0 → yeni maliyet doğrudan giriş fiyatı
        Assert.Equal(42.5m, CostMath.WeightedAvg(0m, 0m, 5m, 42.5m));
    }

    [Fact]
    public void WeightedAvg_NegativeExistingStock_TakesIncomingPrice()
    {
        // negatif mevcut stok ortalamayı bozmamalı → giriş fiyatına düşer
        Assert.Equal(15m, CostMath.WeightedAvg(-3m, 99m, 5m, 15m));
    }

    [Fact]
    public void WeightedAvg_ZeroIncoming_KeepsExistingAvg()
    {
        // geçersiz/0 giriş → maliyet değişmez
        Assert.Equal(20m, CostMath.WeightedAvg(10m, 20m, 0m, 999m));
    }

    [Fact]
    public void WeightedAvg_RoundsToFourDecimals()
    {
        // 1 @ 10 (10) + 2 @ 20 (40) = 50 / 3 = 16.6666...
        Assert.Equal(16.6667m, CostMath.WeightedAvg(1m, 10m, 2m, 20m));
    }

    [Fact]
    public void RollupCost_SumsComponentCostTimesQuantity()
    {
        // Hamburger Menü = 1×Hamburger(15) + 1×Patates(5) + 2×Cola(3.5) = 15 + 5 + 7 = 27
        var components = new[]
        {
            (Quantity: 1m, ComponentCost: 15m),
            (Quantity: 1m, ComponentCost: 5m),
            (Quantity: 2m, ComponentCost: 3.5m)
        };
        Assert.Equal(27m, CostMath.RollupCost(components));
    }

    [Fact]
    public void RollupCost_EmptyRecipe_IsZero()
    {
        Assert.Equal(0m, CostMath.RollupCost(System.Array.Empty<(decimal, decimal)>()));
    }

    [Fact]
    public void MarginAmountAndPercent_ComputedOnSalePrice()
    {
        // maliyet 27, satış 45 → marj 18; % = 18/45*100 = 40
        Assert.Equal(18m, CostMath.MarginAmount(27m, 45m));
        Assert.Equal(40m, CostMath.MarginPercent(27m, 45m));
    }

    [Fact]
    public void MarginPercent_ZeroSalePrice_IsZero()
    {
        Assert.Equal(0m, CostMath.MarginPercent(27m, 0m));
    }

    // ---- BULGU 1: İrsaliye→Fatura fiyat farkı düzeltmesi (PriceCorrectedAvg) ----

    [Fact]
    public void PriceCorrectedAvg_SamePrice_NoChange()
    {
        // Fatura fiyatı = irsaliye fiyatı → fark 0 → maliyet aynen kalır (mevcut doğru davranış korunur)
        Assert.Equal(150m, CostMath.PriceCorrectedAvg(currentAvg: 150m, currentBalance: 20m, valueDelta: 0m));
    }

    [Fact]
    public void PriceCorrectedAvg_HigherInvoice_RaisesAvgByDeltaOverBalance()
    {
        // 10 adet stok, avg 100. Fatura irsaliyeden 200 daha pahalı (delta +200) → +200/10 = +20 → 120
        Assert.Equal(120m, CostMath.PriceCorrectedAvg(100m, 10m, 200m));
    }

    [Fact]
    public void PriceCorrectedAvg_LowerInvoice_LowersAvg()
    {
        // delta negatif → maliyet düşer: 100 + (-150)/10 = 85
        Assert.Equal(85m, CostMath.PriceCorrectedAvg(100m, 10m, -150m));
    }

    [Fact]
    public void PriceCorrectedAvg_NoStock_KeepsAvg()
    {
        // dağıtılacak bakiye yok → maliyete dokunma (sonraki alış düzeltir)
        Assert.Equal(100m, CostMath.PriceCorrectedAvg(100m, 0m, 500m));
        Assert.Equal(100m, CostMath.PriceCorrectedAvg(100m, -5m, 500m));
    }

    [Fact]
    public void PriceCorrectedAvg_RoundsToFourDecimals()
    {
        // 100 + 10/3 = 103.3333...
        Assert.Equal(103.3333m, CostMath.PriceCorrectedAvg(100m, 3m, 10m));
    }

    // ---- BULGU 8: İptal sonrası yeniden hesap (RecomputeAvg) ----

    [Fact]
    public void RecomputeAvg_WeightedOverRemainingPurchases()
    {
        // Kalan alışlar: 10@200 → 2000/10 = 200 (örnek: 10@100 + 10@200 avg150 iken ilk alış iptal → 200)
        Assert.Equal(200m, CostMath.RecomputeAvg(remainingNetValue: 2000m, remainingQty: 10m, fallbackAvg: 150m));
    }

    [Fact]
    public void RecomputeAvg_MultipleRemaining_Weighted()
    {
        // 30@10 (300) + 10@20 (200) = 500/40 = 12.5
        Assert.Equal(12.5m, CostMath.RecomputeAvg(500m, 40m, 999m));
    }

    [Fact]
    public void RecomputeAvg_NoRemainingPricedPurchase_KeepsFallback()
    {
        // Fiyatlı alış geçmişi kalmadı → mevcut maliyet korunur (sonraki alış düzeltir)
        Assert.Equal(150m, CostMath.RecomputeAvg(0m, 0m, 150m));
    }

    [Fact]
    public void RecomputeAvg_RoundsToFourDecimals()
    {
        // 50 / 3 = 16.6666...
        Assert.Equal(16.6667m, CostMath.RecomputeAvg(50m, 3m, 0m));
    }
}
