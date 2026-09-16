using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MindAeonRestWeb.Auth;

/// <summary>Web (cookie) oturumundaki kullanıcı için PWA/API'nin kabul ettiği JWT'yi üretir — MindAeonRest.Api
/// JwtTokenService.Create ile BİREBİR aynı claim seti + imza. Böylece POS ekranlarına (Adisyon/Mutfak/Kasiyer)
/// geçerken tekrar giriş gerekmez (SSO devri). Aynı imza anahtarı JwtKeyProvider ile çözülür.</summary>
public class PwaTokenIssuer(IConfiguration config)
{
    // API JwtTokenService ile aynı claim adları.
    private const string ClaimCompanyCode = "company_code";
    private const string ClaimDbName = "db";
    private const string ClaimBranchId = "branch";
    private const string ClaimKioskNo = "kiosk";
    private const string ClaimFeatureBits = "features";
    private const string ClaimPackage = "package";
    // Gerçek lisans limitleri + expiry — cookie (WebClaims) ve API JWT ile BİREBİR aynı adlar (paylaşımlı sözleşme, FIX C2).
    private const string ClaimLicMaxUsers = "lic_mu";
    private const string ClaimLicMaxBranches = "lic_mb";
    private const string ClaimLicMaxKiosks = "lic_mk";
    private const string ClaimLicExpires = "lic_exp";

    // İmza anahtarı, API JwtBearer doğrulamasıyla AYNI kaynaktan çözülür (JwtKeyProvider): Jwt:Key güçlüyse o,
    // zayıf/placeholder ise makineye özel üretilen anahtar. Böylece SSO ile üretilen token /api'de her zaman doğrulanır.
    private string Key => MindAeonRest.Api.Auth.JwtKeyProvider.Resolve(config);
    private string Issuer => config["Jwt:Issuer"] ?? "MindAeonRest.Api";
    private string Audience => config["Jwt:Audience"] ?? "MindAeonRest.Mobile";
    private int ExpireHours => int.TryParse(config["Jwt:ExpireHours"], out var h) ? h : 12;

    public string Create(int userId, string userName, string companyCode, string dbName,
        int? branchId, IReadOnlyList<string> roles, int kioskNo, long featureBits, string package,
        int maxUsers = 0, int maxBranches = 0, int maxKiosks = 0, DateTime? expiresUtc = null)
    {
        var expires = DateTime.UtcNow.AddHours(ExpireHours);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("name", userName),
            new(ClaimCompanyCode, companyCode),
            new(ClaimDbName, dbName),
            new(ClaimKioskNo, kioskNo.ToString()),
            new(ClaimFeatureBits, featureBits.ToString()),
            new(ClaimPackage, package),
        };
        if (branchId is int b && b > 0) claims.Add(new(ClaimBranchId, b.ToString()));
        // Gerçek lisans limitleri (varsa) PWA token'ına da yazılır → yeniden-girişte doğru limitler (FIX C2).
        // 0 = bilinmiyor/geçilmedi → claim atlanır (geriye-uyum: eski çağrılar bozulmaz).
        if (maxUsers > 0) claims.Add(new(ClaimLicMaxUsers, maxUsers.ToString()));
        if (maxBranches > 0) claims.Add(new(ClaimLicMaxBranches, maxBranches.ToString()));
        if (maxKiosks > 0) claims.Add(new(ClaimLicMaxKiosks, maxKiosks.ToString()));
        // Süresiz → expiresUtc null → claim atlanır.
        if (expiresUtc is { } exp)
            claims.Add(new(ClaimLicExpires, exp.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var r in roles) claims.Add(new(ClaimTypes.Role, r));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
