using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 4b Stok Zekâsı — saf (DB'siz) hesap testleri: satış→reçete rollup teorik tüketim + teorik/gerçek sapma.
/// StockIntelMath, MindAeonRest.Core'daki gerçek üretim koduyla aynıdır (ProjectReference).
/// </summary>
public class StockIntelMathTests
{
    // Menü 100 = 1×Köfte(10) + 2×Ekmek(11); Köfte(10) = 0.15×KıymaKg(20). Ekmek(11) ham (reçetesiz).
    private static IReadOnlyList<(int, decimal)> Recipe(int pid) => pid switch
    {
        100 => new[] { (10, 1m), (11, 2m) },
        10 => new[] { (20, 0.15m) },
        _ => System.Array.Empty<(int, decimal)>()
    };

    [Fact]
    public void ExpandConsumption_RollsUpNestedRecipeToLeafComponents()
    {
        // 3 menü satıldı → Kıyma: 3×1×0.15 = 0.45 kg; Ekmek: 3×2 = 6 adet.
        var result = StockIntelMath.ExpandConsumption(new[] { (100, 3m) }, Recipe);
        Assert.Equal(0.45m, result[20]);
        Assert.Equal(6m, result[11]);
        Assert.False(result.ContainsKey(10));   // ara ürün leaf değil → genişletildi, kendisi sayılmaz
    }

    [Fact]
    public void ExpandConsumption_LeafProductWithoutRecipe_ConsumesItself()
    {
        // Reçetesi olmayan ürün (ör. şişe su 30) doğrudan satılan miktar kadar tüketilir.
        var result = StockIntelMath.ExpandConsumption(new[] { (30, 5m) }, Recipe);
        Assert.Equal(5m, result[30]);
    }

    [Fact]
    public void Variance_ActualAboveTheoretical_IsPositiveLoss()
    {
        // Teorik 0.45, gerçek 0.60 → 0.15 fazla tüketim (fire/kayıp) = +33.33%.
        Assert.Equal(0.15m, StockIntelMath.Variance(theoretical: 0.45m, actual: 0.60m));
        Assert.Equal(33.33m, StockIntelMath.VariancePercent(0.45m, 0.60m));
    }

    [Fact]
    public void VariancePercent_ZeroTheoretical_IsZero()
    {
        Assert.Equal(0m, StockIntelMath.VariancePercent(0m, 5m));
    }
}
