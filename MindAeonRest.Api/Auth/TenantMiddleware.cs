using System.Security.Claims;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;

namespace MindAeonRest.Api.Auth;

/// <summary>Kimliği doğrulanmış her istek için JWT claim'lerinden çok-kiracılı <see cref="Session"/>
/// kapsamını kurar (AsyncLocal). Böylece mevcut servisler (Database.OpenCompany vb.) değişmeden
/// doğru şirket DB'sine ve doğru kullanıcı/şube/kiosk bağlamına göre çalışır.</summary>
public class TenantMiddleware(RequestDelegate next)
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

    private static void ApplySession(ClaimsPrincipal p)
    {
        int.TryParse(p.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out var userId);
        var code = p.FindFirst(JwtTokenService.ClaimCompanyCode)?.Value ?? "";
        var db = p.FindFirst(JwtTokenService.ClaimDbName)?.Value ?? "";
        var name = p.FindFirst("name")?.Value ?? "";
        var roles = p.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        int.TryParse(p.FindFirst(JwtTokenService.ClaimKioskNo)?.Value, out var kioskNo);
        long.TryParse(p.FindFirst(JwtTokenService.ClaimFeatureBits)?.Value, out var featureBits);
        var package = p.FindFirst(JwtTokenService.ClaimPackage)?.Value ?? "Lite";

        Session.CurrentUser = new User { Id = userId, FullName = name, RoleName = roles.FirstOrDefault() ?? "" };
        Session.CurrentCompany = new Company { Code = code, Name = code, DatabaseName = db };
        // Şirket şemasını (Shifts vb. migration'lar) süreç başına bir kez garanti et (doğrudan PWA/MAUI girişleri için de).
        MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(db);
        Session.CurrentRoles = roles;
        Session.CurrentKioskNo = kioskNo <= 0 ? 1 : kioskNo;

        if (int.TryParse(p.FindFirst(JwtTokenService.ClaimBranchId)?.Value, out var branchId))
            Session.CurrentBranch = new CompanyBranch { Id = branchId };

        Enum.TryParse<LicensePackage>(package, out var pkg);

        // Sayısal limitleri token claim'lerinden al (add-on limitleri korunur). Claim yoksa
        // (eski token) paket-tabanına düş → geriye-uyum.
        var lic = new LicenseStatus
        {
            State = LicenseState.Licensed,
            Package = pkg,
            Features = (Feature)featureBits,
            MaxBranches = int.TryParse(p.FindFirst(JwtTokenService.ClaimMaxBranches)?.Value, out var mb) ? mb : PackageMatrix.MaxBranches(pkg),
            MaxUsers = int.TryParse(p.FindFirst(JwtTokenService.ClaimMaxUsers)?.Value, out var mu) ? mu : PackageMatrix.MaxUsers(pkg),
            MaxKiosks = int.TryParse(p.FindFirst(JwtTokenService.ClaimMaxKiosks)?.Value, out var mk) ? mk : PackageMatrix.MaxKiosks(pkg)
        };

        // Expiry claim'i doluysa oturum-içi expiry'yi yansıt (İSTEK BAŞINA HARD-ABORT YOK — gate'ler
        // Session.CurrentLicense.State'e göre davranır; kullanıcı işlem ortasında kilitlenmez).
        var expRaw = p.FindFirst(JwtTokenService.ClaimLicExpiry)?.Value;
        if (!string.IsNullOrWhiteSpace(expRaw) &&
            DateTime.TryParse(expRaw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var exp))
        {
            lic.ExpiresUtc = exp;
            lic.State = DateTime.UtcNow > exp ? LicenseState.Expired : LicenseState.Licensed;
        }

        Session.CurrentLicense = lic;
    }
}
