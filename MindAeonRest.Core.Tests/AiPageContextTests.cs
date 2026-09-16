using MindAeonRest.Services.Ai;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// LocalAiProvider'ın (internetsiz) sayfa/bağlam-farkındalık düşüşü: hiçbir araç anahtar kelimesi
/// eşleşmediğinde VE pageContext doluysa, genel yardım yerine ekrandan türetilmiş yanıt döner.
/// Bu yol tamamen DB'siz (araç Run delegeleri çağrılmaz), saf test edilebilir.
/// </summary>
public class AiPageContextTests
{
    // Hiçbir araç anahtar kelimesine denk gelmeyen, güvenli-anlamsız sorgu.
    private const string NoMatchQuery = "zzqqxx foobar";

    [Fact]
    public void Local_NoToolMatch_WithPageContext_ReturnsPageDerivedAnswer()
    {
        var provider = new LocalAiProvider();
        var ans = provider.Ask(NoMatchQuery, new AiToolRegistry(), "Ürün Kartı");

        Assert.Contains("Ürün Kartı", ans.Text);
        // Genel yardım listesine değil, ekran-yanıtına düşmeli.
        Assert.Contains("ekranındasınız", ans.Text);
    }

    [Fact]
    public void Local_EmptyQuery_WithPageContext_ReturnsPageDerivedAnswer()
    {
        var provider = new LocalAiProvider();
        var ans = provider.Ask("", new AiToolRegistry(), "Cari Hesap");

        Assert.Contains("Cari Hesap", ans.Text);
    }

    [Fact]
    public void Local_NoToolMatch_WithoutPageContext_FallsBackToGenericHelp()
    {
        var provider = new LocalAiProvider();
        var ans = provider.Ask(NoMatchQuery, new AiToolRegistry(), null);

        // Bağlam yoksa eski davranış: genel yardım metni.
        Assert.Contains("sorabilirsiniz", ans.Text);
        Assert.DoesNotContain("ekranındasınız", ans.Text);
    }
}
