using MindAeonRest.Models;

namespace MindAeonRest.Services.Ai;

/// <summary>AI sağlayıcı (Local / Claude / OpenAI). Kullanıcı sorusunu uygun araca yönlendirir.
/// Araçlar SALT-OKUMA; veriyi araçlar üretir (sağlayıcı yalnız niyet/araç seçer).</summary>
public interface IAiProvider
{
    AiProviderType Type { get; }
    /// <summary><paramref name="pageContext"/> doluysa aktif ekran/form bilgisidir; sağlayıcı bağlam
    /// sorularını (bu sayfa ne işe yarar vb.) buna göre yanıtlayabilir.</summary>
    AiAnswer Ask(string userText, AiToolRegistry tools, string? pageContext = null);
}

/// <summary>İnternetsiz kural-tabanlı sağlayıcı: anahtar kelime eşlemesiyle aracı seçer.</summary>
public class LocalAiProvider : IAiProvider
{
    public AiProviderType Type => AiProviderType.Local;

    public AiAnswer Ask(string userText, AiToolRegistry tools, string? pageContext = null)
    {
        string q = (userText ?? "").Trim();
        if (q.Length == 0) return PageHelp(pageContext) ?? Help(tools);
        string lower = q.ToLowerInvariant();

        // En uzun eşleşen anahtar kelimeye sahip aracı seç (daha özgül)
        AiTool? best = null; int bestLen = 0;
        foreach (var t in tools.Tools)
            foreach (var kw in t.Keywords)
                if (lower.Contains(kw, StringComparison.OrdinalIgnoreCase) && kw.Length > bestLen)
                { best = t; bestLen = kw.Length; }

        // Araç eşleşmedi: bağlam varsa (offline sayfa sorusu) sayfadan türetilmiş yanıt, yoksa yardım.
        if (best is null) return PageHelp(pageContext) ?? Help(tools);
        try { return best.Run(q); }
        catch (Exception ex) { return new AiAnswer("İşlem sırasında hata: " + ex.Message); }
    }

    /// <summary>İnternetsiz modda araç eşleşmesi yoksa, aktif ekran bilgisinden (pageContext) türetilmiş
    /// yardımcı bir yanıt üretir. Bağlam boşsa null döner (çağıran genel yardıma düşer).</summary>
    private static AiAnswer? PageHelp(string? pageContext)
    {
        if (string.IsNullOrWhiteSpace(pageContext)) return null;
        return new AiAnswer(
            $"Şu an **{pageContext.Trim()}** ekranındasınız.\n\n" +
            "Bu ekranla ilgili yardım için sorunuzu biraz daha açar mısınız? " +
            "Ayrıca satış/stok/cari gibi konularda şunları sorabilirsiniz: " +
            "\"bugünün özeti\", \"en çok satanlar\", \"kritik stoklar\", \"nakit pozisyon\".");
    }

    private static AiAnswer Help(AiToolRegistry tools)
    {
        string list = string.Join("\n", tools.Tools.Select(t => $"• {t.Description}"));
        return new AiAnswer(
            "Şunları sorabilirsiniz (yazarak veya sesle):\n" + list +
            "\n\nÖrnek: \"bugünün özeti\", \"en çok satanlar\", \"Ahmet bakiye\", \"kola stok\", \"kritik stoklar\", \"nakit pozisyon\".");
    }
}
