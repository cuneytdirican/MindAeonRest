using System.Security.Claims;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRestWeb.Auth;

namespace MindAeonRestWeb.Middleware;

/// <summary>API'deki TenantMiddleware'in COOKIE karşılığı: kimliği doğrulanmış her istek için,
/// cookie claim'lerinden çok-kiracılı <see cref="Session"/> kapsamını (AsyncLocal) kurar.
/// Böylece Core servisleri (Database.OpenCompany vb.) WPF/Api ile birebir aynı çalışır.</summary>
public class WebTenantMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext ctx)
    {
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            using (Session.BeginScope())
            {
                ApplySession(user);
                await next(ctx);
            }
        }
        else
        {
            await next(ctx);
        }
    }

    /// <summary>TenantMiddleware.ApplySession ile AYNI mantık; tek fark KioskNo = 0 (sanal "Web/Merkez").
    /// Razor back-office satış hareketi üretmez; gerçek fiziksel KioskNo damgası PWA/POS'tan gelir.</summary>
    private static void ApplySession(ClaimsPrincipal p)
    {
        int.TryParse(p.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? p.FindFirst("sub")?.Value, out var userId);
        var code = p.FindFirst(WebClaims.CompanyCode)?.Value ?? "";
        var db = p.FindFirst(WebClaims.DbName)?.Value ?? "";
        var name = p.FindFirst("name")?.Value ?? p.Identity?.Name ?? "";
        var roles = p.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        long.TryParse(p.FindFirst(WebClaims.FeatureBits)?.Value, out var featureBits);
        var package = p.FindFirst(WebClaims.Package)?.Value ?? "Lite";

        int.TryParse(p.FindFirst(WebClaims.CompanyId)?.Value, out var companyId);
        Session.CurrentUser = new User { Id = userId, FullName = name, RoleName = roles.FirstOrDefault() ?? "" };
        Session.CurrentCompany = new Company { Id = companyId, Code = code, Name = code, DatabaseName = db };
        // Şirket şemasını (Shifts vb. migration'lar) süreç başına bir kez garanti et — WPF login'in yaptığını web'de de yap.
        MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(db);
        Session.CurrentRoles = roles;
        Session.CurrentKioskNo = 0;   // Web/Merkez sanal kiosk

        if (int.TryParse(p.FindFirst(WebClaims.BranchId)?.Value, out var branchId))
            Session.CurrentBranch = new CompanyBranch { Id = branchId };

        // SysAdmin oturum-içi kiracı geçişi (S3): banner + "Süper Admin'e dön" bu iki claim'den okunur.
        Session.SysAdminHomeCode = p.FindFirst(WebClaims.SysAdminHome)?.Value;
        Session.IsImpersonating = p.FindFirst(WebClaims.Impersonating)?.Value == "1";

        Enum.TryParse<LicensePackage>(package, out var pkg);

        // Gerçek lisans limitleri cookie claim'lerinden okunur (FIX C3). add-on ile büyütülmüş limitler korunur.
        // Claim yoksa (eski cookie) paket-tabanına düş → geriye-uyum.
        int maxUsers = int.TryParse(p.FindFirst(WebClaims.LicMaxUsers)?.Value, out var mu) ? mu : PackageMatrix.MaxUsers(pkg);
        int maxBranches = int.TryParse(p.FindFirst(WebClaims.LicMaxBranches)?.Value, out var mb) ? mb : PackageMatrix.MaxBranches(pkg);
        int maxKiosks = int.TryParse(p.FindFirst(WebClaims.LicMaxKiosks)?.Value, out var mk) ? mk : PackageMatrix.MaxKiosks(pkg);

        // Oturum-içi expiry: cookie'deki lic_exp geçmişse State=Expired (istek başına HARD-ABORT yok; yalnız durum doğru kurulur).
        DateTime? expiresUtc = null;
        var state = LicenseState.Licensed;
        var expRaw = p.FindFirst(WebClaims.LicExpires)?.Value;
        if (!string.IsNullOrEmpty(expRaw) &&
            DateTime.TryParse(expRaw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var exp))
        {
            expiresUtc = exp;
            if (DateTime.UtcNow > exp) state = LicenseState.Expired;
        }

        Session.CurrentLicense = new LicenseStatus
        {
            State = state,
            Package = pkg,
            Features = (Feature)featureBits,
            MaxBranches = maxBranches,
            MaxUsers = maxUsers,
            MaxKiosks = maxKiosks,
            ExpiresUtc = expiresUtc
        };
    }
}
