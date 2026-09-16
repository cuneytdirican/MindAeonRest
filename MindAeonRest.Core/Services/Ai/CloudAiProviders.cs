using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MindAeonRest.Models;

namespace MindAeonRest.Services.Ai;

/// <summary>Bulut LLM sağlayıcılar için ortak taban: LLM'i "araç seçici" olarak kullanır (veri buluta gitmez,
/// yalnız soru + araç adları gider). Anahtar yoksa/çağrı başarısızsa yerel kural-tabanlı yönlendiriciye düşer.</summary>
public abstract class CloudAiProvider : IAiProvider
{
    protected readonly string? ApiKey;
    protected readonly string Model;
    protected readonly double Temperature;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    protected CloudAiProvider(string? apiKey, string? model, string defaultModel, double temperature, double maxTemperature = 2)
    { ApiKey = apiKey; Model = string.IsNullOrWhiteSpace(model) ? defaultModel : model!; Temperature = Math.Clamp(temperature, 0, maxTemperature); }

    public abstract AiProviderType Type { get; }
    protected abstract string ChooseTool(string userText, string toolList, string? pageContext);   // LLM çağrısı → "toolName|arg"

    public AiAnswer Ask(string userText, AiToolRegistry tools, string? pageContext = null)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return new LocalAiProvider().Ask(userText, tools, pageContext);   // anahtar yok → yerel
        try
        {
            string toolList = string.Join("; ", tools.Tools.Select(t => $"{t.Name} = {t.Description}"));
            string raw = ChooseTool(userText, toolList, pageContext);  // "toolName|arg" ya da "answer|<serbest metin>"
            string toolName = raw.Split('|')[0].Trim();

            // Bağlam sorusuna doğrudan yanıt (araç yok): LLM "answer|..." döndürdüyse serbest metni göster.
            if (toolName.Equals("answer", StringComparison.OrdinalIgnoreCase))
            {
                int bar = raw.IndexOf('|');
                string free = bar >= 0 ? raw[(bar + 1)..].Trim() : "";
                if (free.Length > 0) return new AiAnswer(free);
                return new LocalAiProvider().Ask(userText, tools, pageContext);
            }

            var tool = tools.Tools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (tool is null)
            {
                // toolName ne "answer" ne de kayıtlı bir araçla eşleşti. LLM büyük olasılıkla
                // düz prose ile yanıt verdi (sayfa/pageContext soruları için yaygın). Doğru bulut
                // yanıtını sessizce atıp jenerik yerel yardıma düşmek yerine modelin açıklamasını yüzeye çıkar:
                //  - pageContext doluysa (ekranla ilgili soru → prose beklenir), VEYA
                //  - raw'da geçerli bir delimiter yoksa (tek parça prose)
                bool looksLikeProse = !string.IsNullOrWhiteSpace(pageContext) || !raw.Contains('|');
                if (looksLikeProse && !string.IsNullOrWhiteSpace(raw))
                    return new AiAnswer(raw.Trim());
                return new LocalAiProvider().Ask(userText, tools, pageContext);
            }
            return tool.Run(userText);
        }
        catch
        {
            // Anahtar var ama bulut çağrısı başarısız (geçersiz anahtar/bağlantı/limit) → yerele düş ama kullanıcıya bildir
            var ans = new LocalAiProvider().Ask(userText, tools, pageContext);
            ans.Text = "⚠ Bulut AI sağlayıcısına ulaşılamadı (anahtar/bağlantı/limit). Yerel modda yanıtlıyorum:\n\n" + ans.Text;
            return ans;
        }
    }

    protected static string Post(string url, string json, Action<HttpRequestMessage> auth)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        auth(req);
        using var resp = Http.Send(req);
        resp.EnsureSuccessStatusCode();   // 4xx/5xx → catch → yerel + uyarı
        using var sr = new StreamReader(resp.Content.ReadAsStream());
        return sr.ReadToEnd();
    }

    protected string Prompt(string userText, string toolList, string? pageContext)
    {
        // Bağlam bloğu: kullanıcı hangi ekranda? Ekranla ilgili sorularda LLM 'answer|<metin>' ile yanıtlayabilir.
        string ctx = string.IsNullOrWhiteSpace(pageContext)
            ? ""
            : $"KULLANICI ŞU AN ŞU EKRANDA: {pageContext.Trim()}. " +
              "Kullanıcı bu ekranla/formla ilgili bir şey sorarsa (bu sayfa ne işe yarar, bu formu nasıl doldururum vb.), " +
              "araç yerine 'answer|<kısa açıklayıcı yanıt>' biçiminde yanıt ver. ";
        return
            "Sen bir restoran ERP asistanısın. Kullanıcının sorusuna en uygun TEK aracı seç. " +
            "Yalnızca '<arac_adi>|<parametre>' biçiminde yanıtla (parametre yoksa boş bırak, açıklama yazma). " +
            ctx +
            $"Araçlar: {toolList}. Soru: {userText}";
    }
}

/// <summary>Claude (Anthropic) — Messages API.</summary>
public class ClaudeAiProvider : CloudAiProvider
{
    // Anthropic Messages API yalnız [0,1] temperature kabul eder (OpenAI'daki [0,2] değil).
    // Bu yüzden clamp tavanını 1.0 yaparız; aksi halde temp>1 → HTTP 400 → sessizce yerele düşme.
    public ClaudeAiProvider(string? apiKey, string? model, double temperature) : base(apiKey, model, "claude-opus-4-8", temperature, 1.0) { }
    public override AiProviderType Type => AiProviderType.Claude;

    protected override string ChooseTool(string userText, string toolList, string? pageContext)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            max_tokens = 300,
            temperature = Temperature,
            messages = new[] { new { role = "user", content = Prompt(userText, toolList, pageContext) } }
        });
        string body = Post("https://api.anthropic.com/v1/messages", payload, req =>
        {
            req.Headers.Add("x-api-key", ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
        });
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }
}

/// <summary>OpenAI ve TÜM OpenAI-uyumlu sağlayıcılar (OpenRouter, DeepSeek, Grok/xAI, Gemini-openai, Ollama/LM Studio, özel LLM).
/// Tek fark: BaseUrl. Chat Completions şeması ortak.</summary>
public class OpenAiCompatibleProvider : CloudAiProvider
{
    private readonly string _url;
    private readonly AiProviderType _type;

    public OpenAiCompatibleProvider(string? baseUrl, string? apiKey, string? model, AiProviderType type, double temperature, string defaultModel = "gpt-4o-mini")
        : base(apiKey, model, defaultModel, temperature)
    {
        _url = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1/chat/completions" : baseUrl!;
        _type = type;
    }

    public override AiProviderType Type => _type;

    protected override string ChooseTool(string userText, string toolList, string? pageContext)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            max_tokens = 300,
            temperature = Temperature,
            messages = new[] { new { role = "user", content = Prompt(userText, toolList, pageContext) } }
        });
        string body = Post(_url, payload, req => req.Headers.Add("Authorization", "Bearer " + ApiKey));
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
