using MindAeonRest.Helpers;
using Xunit;

namespace MindAeonRest.Core.Tests;

public class CodeHelperTests
{
    [Theory]
    [InlineData("çğıöşü", "CGIOSU")]      // Türkçe küçük harfler
    [InlineData("ÇĞİÖŞÜ", "CGIOSU")]      // Türkçe büyük harfler
    [InlineData("Şube İstanbul", "SUBEISTANBUL")]
    [InlineData("abc-123", "ABC123")]     // ayraç/tire atılır
    [InlineData("  boş kenar  ", "BOSKENAR")] // trim + boşluk atılır
    [InlineData("a.b_c!d", "ABCD")]       // noktalama atılır
    [InlineData("MERKEZ01", "MERKEZ01")]  // zaten normal
    public void Normalize_ProducesUpperAsciiAlnumOnly(string input, string expected)
    {
        Assert.Equal(expected, CodeHelper.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!___...")] // yalnız atılacak karakterler
    public void Normalize_ReturnsEmpty_ForNullWhitespaceOrNoAlnum(string? input)
    {
        Assert.Equal(string.Empty, CodeHelper.Normalize(input));
    }

    [Fact]
    public void Normalize_TurkishDotlessI_MapsToI()
    {
        // 'ı' ve 'İ' → 'I'; kültüre bağlı ToUpper hatalarını önler.
        Assert.Equal("I", CodeHelper.Normalize("ı"));
        Assert.Equal("I", CodeHelper.Normalize("İ"));
    }

    [Theory]
    [InlineData("ok01", true)]
    [InlineData("şube", true)]   // normalize sonrası "SUBE" → geçerli
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("!!!", false)]   // normalize sonrası boş → geçersiz
    public void IsValid_ReflectsNormalizedLength(string? input, bool expected)
    {
        Assert.Equal(expected, CodeHelper.IsValid(input));
    }
}
