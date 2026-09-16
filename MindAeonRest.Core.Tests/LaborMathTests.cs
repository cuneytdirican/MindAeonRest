using System;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 4c Puantaj / Mesai — saf (DB'siz) işçilik + bahşiş dağıtım hesap testleri.
/// LaborMath, MindAeonRest.Core'daki gerçek üretim koduyla aynıdır (ProjectReference).
/// </summary>
public class LaborMathTests
{
    // ---- Çalışılan saat ----
    [Fact]
    public void Hours_FullShift_ComputesElapsedHours()
    {
        var inAt = new DateTime(2026, 7, 3, 9, 0, 0);
        var outAt = new DateTime(2026, 7, 3, 17, 30, 0);   // 8.5 saat
        Assert.Equal(8.5m, LaborMath.Hours(inAt, outAt));
    }

    [Fact]
    public void Hours_OpenOrNonPositive_IsZero()
    {
        var inAt = new DateTime(2026, 7, 3, 9, 0, 0);
        Assert.Equal(0m, LaborMath.Hours(inAt, null));                       // açık mesai
        Assert.Equal(0m, LaborMath.Hours(inAt, inAt.AddHours(-1)));          // çıkış girişten önce
    }

    // ---- Saatlik ücret + işçilik ----
    [Fact]
    public void HourlyRate_DividesSalaryByStandardHours()
    {
        // 39000 maaş / 195 std saat = 200/saat
        Assert.Equal(200m, LaborMath.HourlyRate(39000m, 195m));
        Assert.Equal(0m, LaborMath.HourlyRate(39000m, 0m));   // geçersiz bölen → 0
    }

    [Fact]
    public void LaborCost_RateTimesHours()
    {
        // 200/saat × 8.5 saat = 1700
        Assert.Equal(1700m, LaborMath.LaborCost(200m, 8.5m));
    }

    [Fact]
    public void LaborCostPercent_CostOverRevenue()
    {
        // 1700 işçilik / 8500 ciro × 100 = 20
        Assert.Equal(20m, LaborMath.LaborCostPercent(1700m, 8500m));
        Assert.Equal(0m, LaborMath.LaborCostPercent(1700m, 0m));   // ciro yok → 0
    }

    // ---- Bahşiş havuzu dağıtımı ----
    [Fact]
    public void DistributeTips_SplitsProportionallyToWeights()
    {
        // 300 havuz, ağırlıklar 3:1 (ör. çalışılan saat) → 225 / 75
        var d = LaborMath.DistributeTips(300m, new[] { 3m, 1m });
        Assert.Equal(225m, d[0]);
        Assert.Equal(75m, d[1]);
        Assert.Equal(300m, d[0] + d[1]);   // kuruş kaçağı yok
    }

    [Fact]
    public void DistributeTips_RoundingRemainderGoesToLargestShare_SumsExactly()
    {
        // 100 havuz, 3 eşit ağırlık → 33.33 + 33.33 + 33.34 = 100 (artık en büyük/ilk paya)
        var d = LaborMath.DistributeTips(100m, new[] { 1m, 1m, 1m });
        Assert.Equal(100m, d[0] + d[1] + d[2]);
        Assert.Equal(33.34m, d[0]);   // artık ilk (en büyük ağırlıklı) paya eklenir
        Assert.Equal(33.33m, d[1]);
        Assert.Equal(33.33m, d[2]);
    }

    [Fact]
    public void DistributeTips_NoPositiveWeight_AllZero()
    {
        var d = LaborMath.DistributeTips(500m, new[] { 0m, 0m });
        Assert.Equal(0m, d[0]);
        Assert.Equal(0m, d[1]);
    }
}
