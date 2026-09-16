using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>
/// GÜVENLİ tenant PROVISIONING ucu. CREATE DATABASE gücü taşıdığından JWT/persona ile DEĞİL, ayrı bir
/// dağıtım anahtarı (<c>X-Provision-Key</c>) ile korunur. Bu yüzden [AllowAnonymous] + elle key kontrolü.
///
/// FAIL-CLOSED: config <c>Provisioning:ApiKey</c> BOŞ/YOK ise TÜM uçlar 503 döner (kaza ile açık kalmasın).
/// Yanlış key → 401. Doğru key → ProvisionService'e delege. Enumerasyona kapalı (bilinmeyen kod → 404, sızıntı yok).
/// Sözleşme her iki kümede (Api + Web) AYNIdır.
/// </summary>
[ApiController]
[Route("api/provision")]
[AllowAnonymous]
public class ProvisionController(IConfiguration config) : ControllerBase
{
    private const string KeyHeader = "X-Provision-Key";
    private readonly ProvisionService _provision = new();

    /// <summary>POST /api/provision/tenant → yeni tenant + tenant'a kilitli ilk yönetici. TempPassword yalnız BURADA, tek sefer döner.</summary>
    [HttpPost("tenant")]
    public IActionResult CreateTenant([FromBody] ProvisionTenantRequest req)
    {
        if (Gate() is { } denied) return denied;

        if (req is null || string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name)
            || string.IsNullOrWhiteSpace(req.AdminFullName))
            return BadRequest(new { message = "code, name ve adminFullName zorunludur." });

        try
        {
            var result = _provision.CreateTenant(req.Code, req.Name, req.AdminEmail, req.AdminFullName);
            return Ok(new
            {
                code = result.Code,
                name = result.Name,
                dbName = result.DbName,
                adminUsername = result.AdminUsername,
                tempPassword = result.TempPassword
            });
        }
        catch (TenantAlreadyExistsException)
        {
            // Enumerasyona kapalı: yalnız kimliği doğrulanmış çağıran (geçerli key) buraya gelebilir.
            return Conflict(new { message = "Bu kodla bir tenant zaten mevcut." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/provision/tenants → tüm tenant'ların özeti (salt-okuma denetim).
    /// Mevcut alanlara EK olarak lisans özeti: <c>package</c> (Lite/Standart/Enterprise), <c>licenseState</c>
    /// (Licensed/Trial/TrialExpired/Expired/...), <c>expiresUtc</c> (null = süresiz/deneme).
    /// SALT-OKUMA: <see cref="LicenseService.PeekStatusByCompany"/> kullanılır → listeye bakmak lisanssız tenant'ın
    /// deneme saatini BAŞLATMAZ. Tek tenant'ın lisans okuması patlarsa liste bozulmaz (alanlar null döner).
    /// </summary>
    [HttpGet("tenants")]
    public IActionResult ListTenants()
    {
        if (Gate() is { } denied) return denied;
        var license = new LicenseService();
        var items = _provision.ListTenants()
            .Select(t =>
            {
                MindAeonRest.Models.LicenseStatus? st = null;
                try { st = license.PeekStatusByCompany(t.Code); } catch { /* lisans okunamadı → alanlar null */ }
                return new
                {
                    code = t.Code, name = t.Name, dbName = t.DbName, branchCount = t.BranchCount, userCount = t.UserCount,
                    package = st?.PackageName,
                    licenseState = st?.State.ToString(),
                    expiresUtc = st?.ExpiresUtc
                };
            });
        return Ok(items);
    }

    /// <summary>
    /// POST /api/provision/tenant/{code}/license  { token } → Licence API'nin imzaladığı lisansı tenant'a kurar.
    /// 400: token boş / imza geçersiz / şirket kodu uyuşmuyor / süresi dolmuş · 404: tenant yok.
    /// 200: { code, package, state, expiresUtc, maxUsers, maxBranches, maxKiosks } (kurulum sonrası GetStatusByCompany).
    /// </summary>
    [HttpPost("tenant/{code}/license")]
    public IActionResult InstallTenantLicense(string code, [FromBody] ProvisionLicenseRequest? req)
    {
        if (Gate() is { } denied) return denied;

        var result = _provision.InstallTenantLicense(code, req?.Token);
        if (!result.Ok)
        {
            return result.Error == TenantLicenseInstallError.TenantNotFound
                ? NotFound(new { message = result.Message })
                : BadRequest(new { message = result.Message });
        }

        var st = result.Status!;
        return Ok(new
        {
            code = result.Code,
            package = st.PackageName,
            state = st.State.ToString(),
            expiresUtc = st.ExpiresUtc,
            maxUsers = st.MaxUsers,
            maxBranches = st.MaxBranches,
            maxKiosks = st.MaxKiosks
        });
    }

    /// <summary>GET /api/provision/tenant/{code}/users → tenant kullanıcıları (salt-okuma denetim).</summary>
    [HttpGet("tenant/{code}/users")]
    public IActionResult ListTenantUsers(string code)
    {
        if (Gate() is { } denied) return denied;
        var users = _provision.ListTenantUsers(code);
        if (users is null) return NotFound();
        return Ok(users.Select(u => new { username = u.Username, fullName = u.FullName, roleName = u.RoleName, isActive = u.IsActive }));
    }

    /// <summary>GET /api/provision/tenant/{code}/branches → tenant şubeleri (salt-okuma denetim).</summary>
    [HttpGet("tenant/{code}/branches")]
    public IActionResult ListTenantBranches(string code)
    {
        if (Gate() is { } denied) return denied;
        var branches = _provision.ListTenantBranches(code);
        if (branches is null) return NotFound();
        return Ok(branches.Select(b => new { id = b.Id, name = b.Name, isActive = b.IsActive }));
    }

    /// <summary>
    /// FAIL-CLOSED kapı: config anahtarı boş/yok → 503 (provisioning devre dışı); istek key'i eşleşmiyor → 401.
    /// Geçerliyse null döner (uç çalışır).
    /// </summary>
    private IActionResult? Gate()
    {
        string configured = config["Provisioning:ApiKey"] ?? "";
        if (string.IsNullOrWhiteSpace(configured))
            return StatusCode(503, new { message = "Provisioning devre dışı." });

        string provided = Request.Headers.TryGetValue(KeyHeader, out var v) ? v.ToString() : "";
        if (!FixedEquals(provided, configured))
            return Unauthorized(new { message = "Geçersiz provisioning anahtarı." });

        return null;
    }

    /// <summary>Sabit-süreli karşılaştırma (zamanlama sızıntısına kapalı).</summary>
    private static bool FixedEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
