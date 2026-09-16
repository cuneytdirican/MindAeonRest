using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MindAeonRest.Licence.Portal.Services;

/// <summary>
/// API'yi tüketen typed HttpClient. Login token döndürür (Portal bunu cookie claim'inde saklar);
/// sonraki çağrılarda kullanıcının JWT'sini cookie'den okuyup Bearer olarak ekler.
/// </summary>
public class ApiClient(HttpClient http, IHttpContextAccessor accessor)
{
    public const string JwtClaimType = "api_jwt";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Giriş: e-posta + parola → token + rol bilgisi. Başarısızsa null.</summary>
    public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, JsonOpts, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<LoginResult>(JsonOpts, ct);
    }

    /// <summary>Cookie'deki JWT ile yetkili GET; T'ye deserialize eder.</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        AddBearer(req);
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
    }

    /// <summary>Cookie'deki JWT ile yetkili POST; gövdeyi JSON gönderir, yanıtı TResp'e çevirir.</summary>
    public async Task<TResp?> PostAsync<TResp>(string path, object? body, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        AddBearer(req);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOpts);
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<TResp>(JsonOpts, ct);
    }

    /// <summary>Ham yetkili POST (yanıt gövdesi önemsizse). Başarı durumunu döndürür.</summary>
    public async Task<bool> PostAsync(string path, object? body, CancellationToken ct = default)
    {
        var (ok, _) = await PostResultAsync(path, body, ct);
        return ok;
    }

    /// <summary>
    /// Yetkili POST; gövdeyi TResp'e çevirir VE başarısızlıkta API'nin {message} gövdesini de döndürür.
    /// (Mevcut <see cref="PostAsync{TResp}"/> akışını bozmamak için ayrı bir aşırı yükleme.)
    /// </summary>
    public async Task<(TResp? Value, string? Error)> PostResultAsync<TResp>(string path, object? body, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        AddBearer(req);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOpts);
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (default, await ReadErrorAsync(resp, ct));
        return (await resp.Content.ReadFromJsonAsync<TResp>(JsonOpts, ct), null);
    }

    /// <summary>Ham yetkili POST; başarısızlıkta API'nin {message} gövdesini hata olarak döndürür.</summary>
    public async Task<(bool Ok, string? Error)> PostResultAsync(string path, object? body, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        AddBearer(req);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOpts);
        var resp = await http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) return (true, null);
        return (false, await ReadErrorAsync(resp, ct));
    }

    /// <summary>Başarısız yanıttan {message} alanını çıkarır; gövde JSON değilse durum koduna düşer.</summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        string? message = null;
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<ApiError>(JsonOpts, ct);
            message = doc?.Message;
        }
        catch { /* gövde JSON değilse yoksay */ }
        return string.IsNullOrWhiteSpace(message) ? $"İşlem başarısız ({(int)resp.StatusCode})." : message;
    }

    // ---- Ödeme ayarları: Havale hesabı (yalnız Admin uçları) --------------------
    // Sözleşme: GET/POST /api/v1/payment-config/banktransfer
    // { iban, accountName, bankName, description, active }

    /// <summary>Havale hesabı yapılandırmasını okur (JWT bearer ile). Yoksa null.</summary>
    public Task<BankTransferConfig?> GetBankTransferConfigAsync(CancellationToken ct = default)
        => GetAsync<BankTransferConfig>("/api/v1/payment-config/banktransfer", ct);

    /// <summary>Havale hesabı yapılandırmasını kaydeder; başarısızlıkta API {message} hatasını döndürür.</summary>
    public Task<(bool Ok, string? Error)> SaveBankTransferConfigAsync(BankTransferConfig dto, CancellationToken ct = default)
        => PostResultAsync("/api/v1/payment-config/banktransfer", dto, ct);

    private void AddBearer(HttpRequestMessage req)
    {
        var jwt = accessor.HttpContext?.User?.FindFirst(JwtClaimType)?.Value;
        if (!string.IsNullOrEmpty(jwt))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    }

    private sealed record ApiError(string? Message);
}

/// <summary>API login yanıtı (LoginResp ile eşleşir).</summary>
public record LoginResult(string Token, DateTime ExpiresUtc, string Role, string FullName, string Kind);

/// <summary>
/// Havale hesabı yapılandırması. API DTO'su ile alan-alan eşleşir (Web JSON → camelCase:
/// iban, accountName, bankName, description, active).
/// </summary>
public record BankTransferConfig(
    string? Iban,
    string? AccountName,
    string? BankName,
    string? Description,
    bool Active);
