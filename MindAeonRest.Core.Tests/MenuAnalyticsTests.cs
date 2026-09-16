using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 3a Menü Mühendisliği + Talep Tahmini — saf (DB'siz) mantık testleri.
/// MenuMath matris sınıflandırma (popülerlik × kârlılık) + ForecastMath hareketli ortalama/mevsimsellik.
/// </summary>
public class MenuAnalyticsTests
{
    // ---- MenuMath: matris sınıflandırma ----
    [Fact]
    public void Classify_HighPopHighProfit_IsStar()
    {
        // ort. adet 50, ort. birim marj 10 → adet 80 (yüksek), marj 15 (yüksek) = Yıldız
        Assert.Equal(MenuClass.Star, MenuMath.Classify(80m, 50m, 15m, 10m));
    }

    [Fact]
    public void Classify_HighPopLowProfit_IsPlowhorse()
    {
        // çok satar (80>50) ama az kazandırır (5<10) = İnek
        Assert.Equal(MenuClass.Plowhorse, MenuMath.Classify(80m, 50m, 5m, 10m));
    }

    [Fact]
    public void Classify_LowPopHighProfit_IsPuzzle()
    {
        // az satar (20<50) ama iyi kazandırır (15>10) = Bilmece
        Assert.Equal(MenuClass.Puzzle, MenuMath.Classify(20m, 50m, 15m, 10m));
    }

    [Fact]
    public void Classify_LowPopLowProfit_IsDog()
    {
        Assert.Equal(MenuClass.Dog, MenuMath.Classify(20m, 50m, 5m, 10m));
    }

    [Fact]
    public void Classify_EqualToThreshold_CountsAsHigh()
    {
        // eşiğe eşit (>=) yüksek sayılır → Yıldız
        Assert.Equal(MenuClass.Star, MenuMath.Classify(50m, 50m, 10m, 10m));
    }

    // ---- ForecastMath ----
    [Fact]
    public void MovingAverage_TakesLastWindowOnly()
    {
        var s = new double[] { 10, 20, 30, 40 };
        Assert.Equal(35.0, ForecastMath.MovingAverage(s, 2));   // (30+40)/2
    }

    [Fact]
    public void MovingAverage_EmptySeries_IsZero()
    {
        Assert.Equal(0.0, ForecastMath.MovingAverage(System.Array.Empty<double>(), 7));
    }

    [Fact]
    public void WeeklySeasonalIndices_FlatSeries_AllOne()
    {
        var s = new double[] { 100, 100, 100, 100, 100, 100, 100 };
        var idx = ForecastMath.WeeklySeasonalIndices(s, 0);
        Assert.All(idx, x => Assert.Equal(1.0, x, 6));
    }

    [Fact]
    public void WeeklySeasonalIndices_WeekendPeak_HasHigherIndex()
    {
        // 2 hafta veri: hafta içi 100, hafta sonu (index 5,6) 200 → hafta sonu endeksi > 1
        var s = new double[] { 100, 100, 100, 100, 100, 200, 200,   // wd 0..6
                               100, 100, 100, 100, 100, 200, 200 };
        var idx = ForecastMath.WeeklySeasonalIndices(s, 0);
        Assert.True(idx[5] > 1.0);
        Assert.True(idx[6] > 1.0);
        Assert.True(idx[0] < 1.0);
    }

    [Fact]
    public void ForecastNextDay_AppliesSeasonalityToTargetWeekday()
    {
        // startWeekday=0, 14 gün (2 tam hafta) → sonraki gün target weekday = 14 % 7 = 0 (Pazartesi)
        var s = new double[] { 100, 100, 100, 100, 100, 200, 200,
                               100, 100, 100, 100, 100, 200, 200 };
        double f = ForecastMath.ForecastNextDay(s, startWeekday: 0, window: 14);
        // taban ort = (5*100+2*200)/7 ≈ 128.57; Pazartesi endeksi 100/128.57 ≈ 0.778 → ≈100
        Assert.InRange(f, 95.0, 105.0);
    }

    [Fact]
    public void ForecastNextDays_ReturnsRequestedCount_NonNegative()
    {
        var s = new double[] { 50, 60, 70, 80, 90, 120, 130 };
        var f = ForecastMath.ForecastNextDays(s, 0, 7, 7);
        Assert.Equal(7, f.Length);
        Assert.All(f, x => Assert.True(x >= 0));
    }
}
