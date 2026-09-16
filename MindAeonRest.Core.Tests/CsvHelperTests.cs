using MindAeonRest.Helpers;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>BULGU 11: ortak CSV alan güvenliği — formül/DDE enjeksiyonu nötrleştirme + ayraç kaçışı (saf).</summary>
public class CsvHelperTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"x\")")]   // formül
    [InlineData("+cmd")]                 // formül (sayı değil)
    [InlineData("-2+3")]                 // formül (sayı değil)
    [InlineData("@SUM(A1)")]             // formül
    [InlineData("\tdata")]               // tab ile başlıyor
    public void Field_PrefixesFormulaTriggers(string input)
    {
        string outp = CsvHelper.Field(input);
        Assert.StartsWith("'", outp.TrimStart('"'));   // (tırnaklanmış olsa da) değer tek tırnakla nötrleştirilir
    }

    [Theory]
    [InlineData("1180,00")]   // pozitif tutar
    [InlineData("-500,00")]   // NEGATİF tutar → nötrleştirilMEZ (sayısal içe-aktarım korunur)
    [InlineData("+491")]      // işaretli sayı
    [InlineData("120.001")]   // hesap kodu (nokta ayraçlı)
    public void Field_DoesNotPrefixPlainNumbers(string input)
    {
        Assert.Equal(input, CsvHelper.Field(input));   // değişmeden geçer
    }

    [Fact]
    public void Field_NeutralizesThenEscapesWhenSeparatorPresent()
    {
        // '=' ile başlayıp ayraç da içeren hücre: önce tek tırnak, sonra tırnakla kaçış.
        string outp = CsvHelper.Field("=HYPERLINK(\"a\");b");
        Assert.StartsWith("\"'=", outp);         // tırnak açılışı + nötrleştirme
        Assert.Contains("\"\"", outp);            // içteki tırnak ikiye katlandı
    }

    [Fact]
    public void Field_PlainTextUnchanged()
    {
        Assert.Equal("Acme A.Ş.", CsvHelper.Field("Acme A.Ş."));
    }
}
