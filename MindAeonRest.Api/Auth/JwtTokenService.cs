using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MindAeonRest.Models;

namespace MindAeonRest.Api.Auth;

/// <summary>JWT üretir. Token, çok-kiracılı bağlamı (şirket DB'si, şube, kiosk, roller, lisans) taşır;
/// TenantMiddleware her istekte bunları Session kapsamına yazar.</summary>
public class JwtTokenService(IConfiguration config)
{
    public const string ClaimCompanyCode = "company_code";
    public const string ClaimDbName = "db";
    public const string ClaimBranchId = "branch";
    public const string ClaimKioskNo = "kiosk";
    public const string ClaimFeatureBits = "features";
    public const string ClaimPackage = "package";
    // Sayısal lisans limitleri + expiry (Web ajanı AYNI adları kullanır — paylaşımlı sözleşme).
    // Böylece middleware paketten yeniden hesaplamaz; add-on limitleri ve gerçek bitiş korunur.
    public const string ClaimMaxUsers = "lic_mu";
    public const string ClaimMaxBranches = "lic_mb";
    public const string ClaimMaxKiosks = "lic_mk";
    public const string ClaimLicExpiry = "lic_exp";   // ISO-8601 round-trip ("o"); süresiz ise atlanır

    private string Key => JwtKeyProvider.Resolve(config);   // placeholder/zayıfsa makineye özel güçlü anahtar
    private string Issuer => config["Jwt:Issuer"] ?? "MindAeonRest.Api";
    private string Audience => config["Jwt:Audience"] ?? "MindAeonRest.Mobile";
    private int ExpireHours => int.TryParse(config["Jwt:ExpireHours"], out var h) ? h : 12;

    public (string Token, DateTime ExpiresUtc) Create(User user, Company company, CompanyBranch? branch,
        IReadOnlyList<string> roles, int kioskNo, long featureBits, string package, LicenseStatus? lic = null)
    {
        var expires = DateTime.UtcNow.AddHours(ExpireHours);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("name", user.FullName ?? user.Username),
            new(ClaimCompanyCode, company.Code),
            new(ClaimDbName, company.DatabaseName),
            new(ClaimKioskNo, kioskNo.ToString()),
            new(ClaimFeatureBits, featureBits.ToString()),
            new(ClaimPackage, package),
        };
        // Gerçek sayısal limitler + expiry (varsa) → middleware bunları okur (paketten yeniden hesaplamaz).
        if (lic is not null)
        {
            claims.Add(new(ClaimMaxUsers, lic.MaxUsers.ToString()));
            claims.Add(new(ClaimMaxBranches, lic.MaxBranches.ToString()));
            claims.Add(new(ClaimMaxKiosks, lic.MaxKiosks.ToString()));
            if (lic.ExpiresUtc is { } exp) claims.Add(new(ClaimLicExpiry, exp.ToString("o")));
        }
        if (branch is not null) claims.Add(new(ClaimBranchId, branch.Id.ToString()));
        foreach (var r in roles) claims.Add(new(ClaimTypes.Role, r));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
}
