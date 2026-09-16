using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MindAeonRest.Helpers;

namespace MindAeonRest.Services;

/// <summary>Lisans portalı API istemcisi (online aktivasyon / doğrulama / ciro raporu).
/// Adres AppConfig.LicenceApiUrl'den; boşsa online devre dışı (yalnız offline kullanılır).</summary>
public class LicenceApiClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(AppConfig.LicenceApiUrl);
    private static string Base => AppConfig.LicenceApiUrl;

    public record ActivateResult(string Status, string? Token, string? Message);
    public record ValidateResult(string Status, DateTime? ExpiresUtc);

    /// <summary>API'ye erişilebiliyor mu (ping).</summary>
    public async Task<bool> PingAsync()
    {
        if (!IsConfigured) return false;
        try { var r = await _http.GetAsync($"{Base}/api/ping"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    /// <summary>Talep anahtarıyla online aktivasyon. Dönüş: status "issued"(token dolu) / "pending" / "none" / "error".</summary>
    public async Task<ActivateResult> ActivateAsync(string requestToken)
    {
        if (!IsConfigured) return new("error", null, "Lisans API adresi tanımlı değil.");
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{Base}/api/v1/licenses/activate", new { requestToken }, Json);
            if (!resp.IsSuccessStatusCode) return new("error", null, $"Sunucu hatası ({(int)resp.StatusCode}).");
            var r = await resp.Content.ReadFromJsonAsync<ActivateResult>(Json);
            return r ?? new("error", null, "Yanıt çözülemedi.");
        }
        catch (Exception ex) { return new("error", null, "Sunucuya ulaşılamadı: " + ex.Message); }
    }

    /// <summary>Lisans durumunu sunucuda doğrula (heartbeat): Active / Revoked / Expired / WrongMachine / NotFound / error.</summary>
    public async Task<ValidateResult> ValidateAsync(string licenseCode, string hardwareId)
    {
        if (!IsConfigured) return new("error", null);
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{Base}/api/v1/licenses/validate", new { licenseCode, hardwareId }, Json);
            if (!resp.IsSuccessStatusCode) return new("error", null);
            return await resp.Content.ReadFromJsonAsync<ValidateResult>(Json) ?? new("error", null);
        }
        catch { return new("error", null); }
    }

    /// <summary>Periyodik kullanım/ciro raporu (en iyi çaba; hata yutulur).</summary>
    public async Task ReportUsageAsync(string companyCode, string hardwareId, int branchCount, int userCount, int kioskCount, decimal revenue, string period)
    {
        if (!IsConfigured) return;
        try
        {
            await _http.PostAsJsonAsync($"{Base}/api/v1/usage",
                new { companyCode, hardwareId, branchCount, userCount, kioskCount, revenue, period }, Json);
        }
        catch { /* en iyi çaba */ }
    }
}
