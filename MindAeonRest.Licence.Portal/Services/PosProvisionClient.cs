using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MindAeonRest.Licence.Portal.Services;

/// <summary>
/// POS tarafındaki (MindAeonRestWeb) provisioning API'sini SUNUCU-TARAFI tüketen typed istemci.
/// Kimlik: her istekte <c>X-Provision-Key</c> header'ı (config <c>Pos:ProvisionKey</c>). Bu anahtar
/// yalnız Portal sunucusunda kalır; tarayıcıya asla gönderilmez (sayfalar OnGet/OnPost içinde çağırır).
/// Portal'ın kendi kullanıcı JWT'siyle ilgisi yoktur — provisioning ayrı bir güven sınırıdır.
///
/// Sözleşme (POS ile birebir):
///   POST /api/provision/tenant                 { code, name, adminEmail, adminFullName }
///        → 200 { code, name, dbName, adminUsername, tempPassword } | 409 kod zaten var
///   GET  /api/provision/tenants                → [{ code, name, dbName, branchCount, userCount, package?, licenseState?, expiresUtc? }]
///   POST /api/provision/tenant/{code}/license  { token }
///        → 200 { code, package, state, expiresUtc, maxUsers, maxBranches, maxKiosks } | 400 imza/kod/süre | 404 tenant yok
///   GET  /api/provision/tenant/{code}/users    → [{ username, fullName, roleName, isActive }]
///   GET  /api/provision/tenant/{code}/branches → [{ id, name, isActive }]
///
/// FAIL-CLOSED: <c>Pos:ProvisionKey</c> boş/yoksa hiçbir istek yapılmaz; metotlar net
/// "yapılandırılmamış" hatası döndürür (POS ucu da anahtar yoksa 503 verir).
/// </summary>
public sealed class PosProvisionClient(HttpClient http, IConfiguration config)
{
    private const string KeyHeader = "X-Provision-Key";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Config'deki provisioning anahtarı. Boşsa istemci devre dışıdır (fail-closed).</summary>
    private string? ProvisionKey => config["Pos:ProvisionKey"];

    /// <summary>Anahtar yapılandırılmış mı? Sayfalar bunu kullanarak formu/tabloyu gizleyebilir.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProvisionKey);

    private const string NotConfigured =
        "POS provisioning yapılandırılmamış (Pos:ProvisionKey boş). Bu anahtar sunucuda tanımlanmalıdır.";

    /// <summary>Yeni lisanslı tenant + admin kullanıcı oluşturur. Dönen geçici parola BİR KEZ gösterilir.</summary>
    public Task<(CreateTenantResult? Value, string? Error)> CreateTenantAsync(
        CreateTenantRequest dto, CancellationToken ct = default)
        => SendJsonAsync<CreateTenantResult>(HttpMethod.Post, "/api/provision/tenant", dto, ct);

    /// <summary>Tüm tenant'ların özetini listeler (oversight tablosu).</summary>
    public Task<(IReadOnlyList<TenantSummary>? Value, string? Error)> ListTenantsAsync(CancellationToken ct = default)
        => GetListAsync<TenantSummary>("/api/provision/tenants", ct);

    /// <summary>Bir tenant'ın kullanıcılarını listeler.</summary>
    public Task<(IReadOnlyList<TenantUser>? Value, string? Error)> ListTenantUsersAsync(string code, CancellationToken ct = default)
        => GetListAsync<TenantUser>($"/api/provision/tenant/{Uri.EscapeDataString(code)}/users", ct);

    /// <summary>Bir tenant'ın şubelerini listeler.</summary>
    public Task<(IReadOnlyList<TenantBranch>? Value, string? Error)> ListTenantBranchesAsync(string code, CancellationToken ct = default)
        => GetListAsync<TenantBranch>($"/api/provision/tenant/{Uri.EscapeDataString(code)}/branches", ct);

    /// <summary>Licence API'nin imzaladığı lisans token'ını POS tenant'ına kurar; kurulum sonrası lisans durumunu döndürür.</summary>
    public Task<(TenantLicenseStatus? Value, string? Error)> InstallLicenseAsync(string code, string token, CancellationToken ct = default)
        => SendJsonAsync<TenantLicenseStatus>(HttpMethod.Post,
            $"/api/provision/tenant/{Uri.EscapeDataString(code)}/license", new { token }, ct);

    // ---- alt seviye ----------------------------------------------------------

    private async Task<(IReadOnlyList<T>? Value, string? Error)> GetListAsync<T>(string path, CancellationToken ct)
    {
        var (value, error) = await SendJsonAsync<List<T>>(HttpMethod.Get, path, body: null, ct);
        return (value, error);
    }

    private async Task<(TResp? Value, string? Error)> SendJsonAsync<TResp>(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!IsConfigured)
            return (default, NotConfigured);

        using var req = new HttpRequestMessage(method, path);
        req.Headers.TryAddWithoutValidation(KeyHeader, ProvisionKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOpts);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            return (default, $"POS provisioning servisine ulaşılamadı: {ex.Message}");
        }

        if (resp.IsSuccessStatusCode)
            return (await resp.Content.ReadFromJsonAsync<TResp>(JsonOpts, ct), null);

        return (default, await ReadErrorAsync(resp, ct));
    }

    /// <summary>HTTP durumunu okunur bir mesaja çevirir; gövdede {message} varsa onu tercih eder.</summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        string? message = null;
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<ProvisionError>(JsonOpts, ct);
            message = doc?.Message;
        }
        catch { /* gövde JSON değilse yoksay */ }

        if (!string.IsNullOrWhiteSpace(message)) return message!;

        return resp.StatusCode switch
        {
            HttpStatusCode.Conflict => "Bu kod ile bir tenant zaten var.",
            HttpStatusCode.Unauthorized => "Provisioning anahtarı reddedildi (401). Pos:ProvisionKey hatalı.",
            HttpStatusCode.ServiceUnavailable => "POS tarafında provisioning devre dışı (503).",
            _ => $"İşlem başarısız ({(int)resp.StatusCode})."
        };
    }

    private sealed record ProvisionError(string? Message);
}

// ---- DTO'lar (POS sözleşmesiyle birebir; camelCase JSON) --------------------

/// <summary>Yeni tenant oluşturma isteği.</summary>
public sealed record CreateTenantRequest(string Code, string Name, string AdminEmail, string AdminFullName);

/// <summary>Yeni tenant oluşturma sonucu. TempPassword yalnız BİR KEZ döner; hiçbir yerde saklanmaz.</summary>
public sealed record CreateTenantResult(string Code, string Name, string DbName, string AdminUsername, string TempPassword);

/// <summary>Oversight tablosu satırı. Lisans alanları OPSİYONEL: eski POS yanıtlarında yoksa null kalır
/// (Package: Lite/Standart/Enterprise; LicenseState: Licensed/Trial/TrialExpired/Expired/...; ExpiresUtc null = süresiz).</summary>
public sealed record TenantSummary(string Code, string Name, string DbName, int BranchCount, int UserCount,
    string? Package = null, string? LicenseState = null, DateTime? ExpiresUtc = null);

/// <summary>POS'a lisans kurulumu sonrası durum (POST /api/provision/tenant/{code}/license yanıtı).</summary>
public sealed record TenantLicenseStatus(string? Code, string? Package, string? State, DateTime? ExpiresUtc,
    int MaxUsers, int MaxBranches, int MaxKiosks);

/// <summary>Tenant kullanıcı satırı.</summary>
public sealed record TenantUser(string Username, string FullName, string RoleName, bool IsActive);

/// <summary>Tenant şube satırı.</summary>
public sealed record TenantBranch(int Id, string Name, bool IsActive);
