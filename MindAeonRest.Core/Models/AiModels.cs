namespace MindAeonRest.Models;

public enum AiProviderType { Local = 0, Claude = 1, OpenAI = 2, Custom = 3 }

public static class AiProviderTypeExtensions
{
    public static string ToTr(this AiProviderType t) => t switch
    {
        AiProviderType.Claude => "Claude (Anthropic)",
        AiProviderType.OpenAI => "OpenAI",
        AiProviderType.Custom => "Özel / OpenAI-uyumlu (OpenRouter, DeepSeek, Grok, Gemini, yerel LLM…)",
        _ => "Yerel (internetsiz)"
    };
}

/// <summary>AI Hub ayarları (şirket DB, tek satır). Custom = OpenAI-uyumlu herhangi bir sağlayıcı (BaseUrl + anahtar + model).</summary>
public class AiSettings
{
    public AiProviderType Provider { get; set; } = AiProviderType.Local;
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }             // Custom: tam chat/completions uç noktası
    public double Temperature { get; set; } = 0.2;   // yaratıcılık sınırı (0=tutarlı/kesin … 1+=yaratıcı)
    public bool VoiceOutput { get; set; } = true;    // cevabı sesli oku (TTS)
    public bool VoiceInput { get; set; }             // mikrofonla soru (STT, bulut)
}

/// <summary>Özel sağlayıcı için hazır şablonlar (OpenAI-uyumlu uç noktalar).</summary>
public class AiPreset
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public static List<AiPreset> All => new()
    {
        new() { Name = "OpenRouter", BaseUrl = "https://openrouter.ai/api/v1/chat/completions", Model = "openai/gpt-4o-mini" },
        new() { Name = "DeepSeek", BaseUrl = "https://api.deepseek.com/chat/completions", Model = "deepseek-chat" },
        new() { Name = "Grok (xAI)", BaseUrl = "https://api.x.ai/v1/chat/completions", Model = "grok-2-latest" },
        new() { Name = "Gemini (OpenAI-uyumlu)", BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", Model = "gemini-1.5-flash" },
        new() { Name = "Yerel (Ollama / LM Studio)", BaseUrl = "http://localhost:11434/v1/chat/completions", Model = "llama3.1" },
    };
}

/// <summary>AI sohbet mesajı (kullanıcı veya asistan). Asistan cevabı opsiyonel tablo taşıyabilir.</summary>
public class AiMessage
{
    public bool IsUser { get; set; }
    public string Text { get; set; } = "";
    public ReportTable? Table { get; set; }
    public DateTime At { get; set; } = DateTime.Now;

    public string TimeText => At.ToString("HH:mm");
    public bool HasTable => Table is { Rows.Count: > 0 };

    /// <summary>Tabloyu sohbette göstermek için sade hizalı metin (ilk 20 satır).</summary>
    public string TableText
    {
        get
        {
            if (Table is null || Table.Columns.Count == 0) return "";
            var cols = Table.Columns;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join("  |  ", cols.Select(c => c.Header)));
            foreach (var row in Table.Rows.Take(20))
                sb.AppendLine(string.Join("  |  ", cols.Select((c, i) =>
                {
                    object? v = i < row.Length ? row[i] : null;
                    if (v is null) return "";
                    return c.Numeric && v is IFormattable f ? f.ToString(c.Effective, new System.Globalization.CultureInfo("tr-TR")) : v.ToString() ?? "";
                })));
            if (Table.Rows.Count > 20) sb.AppendLine($"… (+{Table.Rows.Count - 20} satır)");
            return sb.ToString().TrimEnd();
        }
    }
}

/// <summary>Bir AI aracının (tool) ürettiği cevap: metin + opsiyonel tablo.</summary>
public class AiAnswer
{
    public string Text { get; set; } = "";
    public ReportTable? Table { get; set; }
    public AiAnswer() { }
    public AiAnswer(string text, ReportTable? table = null) { Text = text; Table = table; }
}

/// <summary>AI'nın çağırabileceği deterministik araç tanımı (LLM function-calling + yerel niyet eşleme).</summary>
public class AiTool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Keywords { get; set; } = Array.Empty<string>();   // yerel niyet eşleme için
    public Func<string, AiAnswer> Run { get; set; } = _ => new AiAnswer("");
}
