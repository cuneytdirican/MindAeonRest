using MindAeonRest.Models;
using MindAeonRest.Services.Ai;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// CloudAiProvider'ın iki dar düzeltmesi için saf testler (HTTP yok):
///  - BULGU 7: LLM düz prose ile yanıtladığında (araç/"answer" eşleşmesi yok) yanıt atılmaz, yüzeye çıkar.
///  - BULGU 8: temperature clamp tavanı sağlayıcıya özeldir (Claude 1.0, OpenAI-uyumlu 2.0).
/// ChooseTool override edildiği için ağ çağrısı yapılmaz; tamamen offline.
/// </summary>
public class CloudAiProviderTests
{
    // ChooseTool'u sabit bir "raw" döndürecek şekilde ezen sahte bulut sağlayıcı (HTTP çağrısı yok).
    private sealed class FakeCloud : CloudAiProvider
    {
        private readonly string _raw;
        public FakeCloud(string raw) : base("fake-key", null, "test-model", 0.5) { _raw = raw; }
        public override AiProviderType Type => AiProviderType.Claude;
        protected override string ChooseTool(string userText, string toolList, string? pageContext) => _raw;
    }

    // Temperature (protected) alanını okumak için ince prob'lar.
    private sealed class ProbeClaude : ClaudeAiProvider
    {
        public ProbeClaude(double temp) : base("k", null, temp) { }
        public double Temp => Temperature;
    }

    private sealed class ProbeOpenAi : OpenAiCompatibleProvider
    {
        public ProbeOpenAi(double temp) : base(null, "k", null, AiProviderType.OpenAI, temp) { }
        public double Temp => Temperature;
    }

    // ---- BULGU 7: prose-fallback ----

    [Fact]
    public void Ask_PlainProse_WithPageContext_SurfacesModelProse()
    {
        const string prose = "Bu ekran ürün kartıdır; ürünü kaydetmek için Code ve ad girin.";
        var provider = new FakeCloud(prose);

        var ans = provider.Ask("bu sayfa ne işe yarar?", new AiToolRegistry(), "Ürün Kartı");

        // Modelin prose açıklaması aynen yüzeye çıkmalı, LocalAiProvider jenerik yardımına düşmemeli.
        Assert.Equal(prose, ans.Text);
        Assert.DoesNotContain("sorabilirsiniz", ans.Text);
        Assert.DoesNotContain("ekranındasınız", ans.Text);
    }

    [Fact]
    public void Ask_PlainProse_NoDelimiter_NoPageContext_SurfacesModelProse()
    {
        const string prose = "Toplam ciro dün 12.500 TL idi.";
        var provider = new FakeCloud(prose);

        var ans = provider.Ask("dün ciro neydi", new AiToolRegistry(), null);

        // Delimiter yok → tek parça prose kabul edilir, atılmaz.
        Assert.Equal(prose, ans.Text);
    }

    [Fact]
    public void Ask_AnswerPrefixed_ReturnsFreeText()
    {
        var provider = new FakeCloud("answer|Merhaba, size nasıl yardımcı olabilirim?");

        var ans = provider.Ask("selam", new AiToolRegistry(), null);

        Assert.Equal("Merhaba, size nasıl yardımcı olabilirim?", ans.Text);
    }

    // ---- BULGU 8: sağlayıcıya özel temperature clamp ----

    [Fact]
    public void Claude_Temperature_ClampedTo_One()
    {
        // Anthropic yalnız [0,1] kabul eder; 1.5 istenirse 1.0'a kırpılmalı (HTTP 400 olmasın).
        Assert.Equal(1.0, new ProbeClaude(1.5).Temp, 3);
    }

    [Fact]
    public void Claude_Temperature_WithinRange_Preserved()
    {
        Assert.Equal(0.7, new ProbeClaude(0.7).Temp, 3);
    }

    [Fact]
    public void OpenAi_Temperature_AllowsUpToTwo()
    {
        // OpenAI-uyumlu sağlayıcılar [0,2] kabul eder; 1.5 korunmalı.
        Assert.Equal(1.5, new ProbeOpenAi(1.5).Temp, 3);
    }
}
