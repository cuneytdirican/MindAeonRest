using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Models;

/// <summary>
/// Admin sayfaları için ince API yardımcısı. ApiClient yalnız GET/POST sunduğundan,
/// PUT/DELETE gerektiren admin işlemleri (bayi güncelle, fiyat sil vb.) burada kendi
/// HttpClient'ı ile çözülür. JWT, oturum çerezindeki claim'den (ApiClient.JwtClaimType) okunur.
/// Bu sınıf Admin klasörüne yakındır; başka rolün dosyalarına dokunmaz (çakışma yasağı).
/// </summary>
public sealed class AdminApi(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private HttpClient Create()
    {
        var client = factory.CreateClient("AdminApi");
        var baseUrl = config["ApiBaseUrl"] ?? "http://localhost:5100";
        client.BaseAddress = new Uri(baseUrl);
        var jwt = accessor.HttpContext?.User?.FindFirst(ApiClient.JwtClaimType)?.Value;
        if (!string.IsNullOrEmpty(jwt))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var client = Create();
        var resp = await client.GetAsync(path, ct);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
    }

    public async Task<(bool Ok, string? Error)> PutAsync(string path, object body, CancellationToken ct = default)
    {
        using var client = Create();
        var resp = await client.PutAsJsonAsync(path, body, JsonOpts, ct);
        return await ReadResultAsync(resp, ct);
    }

    public async Task<(bool Ok, string? Error)> PostAsync(string path, object? body, CancellationToken ct = default)
    {
        using var client = Create();
        HttpResponseMessage resp = body is null
            ? await client.PostAsync(path, null, ct)
            : await client.PostAsJsonAsync(path, body, JsonOpts, ct);
        return await ReadResultAsync(resp, ct);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string path, CancellationToken ct = default)
    {
        using var client = Create();
        var resp = await client.DeleteAsync(path, ct);
        return await ReadResultAsync(resp, ct);
    }

    private static async Task<(bool, string?)> ReadResultAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return (true, null);
        string? message = null;
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<ApiError>(JsonOpts, ct);
            message = doc?.Message;
        }
        catch { /* gövde JSON değilse yoksay */ }
        return (false, message ?? $"İşlem başarısız ({(int)resp.StatusCode}).");
    }

    private sealed record ApiError(string? Message);
}
