using MindAeonRestWeb.Infrastructure;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// MindAeonRestWeb.Infrastructure.Num — kültürden bağımsız sayı ayrıştırma.
/// Kaynak dosya (Num.cs) test projesine BAĞLANTILI (linked) derlenir; gerçek davranış test edilir.
/// </summary>
public class NumTests
{
    [Theory]
    [InlineData("125.0000", 125)]     // type=number, tek nokta = ondalık
    [InlineData("125,50", 125.5)]     // tr: virgül ondalık
    [InlineData("1.250,50", 1250.5)]  // tr: nokta binlik, virgül ondalık
    [InlineData("1,250.50", 1250.5)]  // en: virgül binlik, nokta ondalık
    [InlineData("125", 125)]          // ayraçsız tam sayı
    [InlineData("0", 0)]
    [InlineData("-42,5", -42.5)]      // negatif işaret
    [InlineData("  1.000.000,25  ", 1000000.25)] // çok binlik + trim
    public void TryParseDecimal_ParsesCultureIndependently(string raw, double expected)
    {
        Assert.True(Num.TryParseDecimal(raw, out decimal value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void TryParseDecimal_ReturnsFalse_ForEmptyOrInvalid(string? raw)
    {
        Assert.False(Num.TryParseDecimal(raw, out decimal value));
        Assert.Equal(0m, value);
    }

    [Theory]
    [InlineData("₺125,50", 125.5)]  // para birimi sembolü atılır
    [InlineData("%18", 18)]         // yüzde sembolü atılır
    public void TryParseDecimal_StripsCurrencyAndPercentSymbols(string raw, double expected)
    {
        Assert.True(Num.TryParseDecimal(raw, out decimal value));
        Assert.Equal((decimal)expected, value);
    }

    [Fact]
    public void ParseOrZero_ReturnsZero_ForInvalid()
    {
        Assert.Equal(0m, Num.ParseOrZero("çöp"));
        Assert.Equal(1250.5m, Num.ParseOrZero("1.250,50"));
    }

    [Fact]
    public void Normalize_ReturnsNull_ForEmpty()
    {
        Assert.Null(Num.Normalize("   "));
    }
}
