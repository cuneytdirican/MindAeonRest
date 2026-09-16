using MindAeonRest.Models;

namespace MindAeonRest.Services.Ai;

/// <summary>AI Hub orkestrasyonu: ayara göre sağlayıcı seçer, kullanıcı sorusunu deterministik araçlara yönlendirir.
/// Salt-okuma: araçlar yalnız sorgular/öneri üretir.</summary>
public class AiHubService
{
    private readonly AiToolRegistry _tools = new();

    public static IAiProvider Build(AiSettings s) => s.Provider switch
    {
        AiProviderType.Claude => new ClaudeAiProvider(s.ApiKey, s.Model, s.Temperature),
        AiProviderType.OpenAI => new OpenAiCompatibleProvider(null, s.ApiKey, s.Model, AiProviderType.OpenAI, s.Temperature),
        AiProviderType.Custom => new OpenAiCompatibleProvider(s.BaseUrl, s.ApiKey, s.Model, AiProviderType.Custom, s.Temperature),
        _ => new LocalAiProvider()
    };

    /// <summary>Kullanıcı sorusunu yanıtlar. <paramref name="pageContext"/> doluysa (web/masaüstü ekran
    /// bilgisi), sağlayıcı "bu sayfa ne işe yarar / bu formu nasıl doldururum" gibi bağlam sorularını da
    /// yanıtlayabilir. Sunucu-taraf yetki kapısı: AI yetkisi yoksa erken hata (savunma-derinliği;
    /// UI çağıranları zaten kontrol eder).</summary>
    public AiAnswer Ask(string userText, string? pageContext = null)
    {
        if (!MindAeonRest.Helpers.Permissions.Ai)
            throw new InvalidOperationException("AI kullanım yetkiniz yok.");

        var settings = new AiSettingsService().Get();
        var provider = Build(settings);
        return provider.Ask(userText, _tools, pageContext);
    }
}
