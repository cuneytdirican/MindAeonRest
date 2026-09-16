using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MindAeonRest.Api.Auth;

/// <summary>FAZ 5a Sadakat Portalı — müşteri (telefon+OTP) girişinin KISITLI-SCOPE JWT altyapısı. <see cref="PublicTokenService"/>
/// deseninin birebir eşleniği: AYRI audience (MindAeonRest.Public) taşır → normal /api [Authorize] JwtBearer bu token'ı
/// REDDEDER; yalnız [AllowAnonymous] portal uçlarında ELLE doğrulanır (yetki yükselmesi yok). İki token türü:
/// <list type="bullet">
/// <item><b>Giriş (entry)</b>: restoran tarafından üretilir, şirket+şube gömülüdür, uzun ömür — şirket kodu URL'de AÇIK
/// geçmez (enumerasyon yok). Müşteri OTP istemeden önce yalnız bağlamı taşır.</item>
/// <item><b>Oturum (portal)</b>: OTP doğrulanınca üretilir, müşteri kimliği (customerId) gömülüdür, kısa ömür — puan/geçmiş/
/// yeniden-sipariş uçları bunu doğrular → müşteri yalnız KENDİ kartını görür (IDOR imkânsız).</item></list>
/// İmza anahtarı normal JWT ile AYNI kaynaktan (JwtKeyProvider) çözülür. Ayrı dosya/servis: eşzamanlı public-token
/// genişlemeleriyle (rezervasyon/anket) birleştirme çakışması yaşamamak için PublicTokenService'ten bağımsız tutulur.</summary>
public class PortalTokenService(IConfiguration config)
{
    // Purpose/claim adları PublicTokenService ile TEK KAYNAK (aynı sözlük); yeniden tanımlamayız.
    private const string PortalAudience = "MindAeonRest.Public";

    private string Key => JwtKeyProvider.Resolve(config);
    private string Issuer => config["Jwt:Issuer"] ?? "MindAeonRest.Api";
    /// <summary>Giriş (entry) token ömrü (gün) — QR/bağlantı basılıp paylaşıldığından uzun. Config Public:TokenDays (vars. 180).</summary>
    private int EntryDays => int.TryParse(config["Public:TokenDays"], out var d) && d > 0 ? d : 180;
    /// <summary>Müşteri oturum token ömrü (dakika). Config Portal:SessionMinutes, varsayılan 120.</summary>
    private int SessionMinutes => int.TryParse(config["Portal:SessionMinutes"], out var m) && m > 0 ? m : 120;

    private string Sign(IEnumerable<Claim> claims, DateTime expires)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, PortalAudience, claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private ClaimsPrincipal? ValidatePrincipal(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Issuer,
                ValidAudience = PortalAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);
        }
        catch { return null; }
    }

    /// <summary>Restoran back-office'inde üretilen portal GİRİŞ token'ı (şirket+şube gömülü, masasız, uzun ömür).
    /// Müşteriye verilen /Portal?t={token} bağlantısında taşınır; şirket kodu URL'de açık geçmez (enumerasyon yok).</summary>
    public string CreateEntryToken(string companyCode, string dbName, int branchId)
    {
        var claims = new List<Claim>
        {
            new(PublicTokenService.ClaimCompanyCode, companyCode),
            new(PublicTokenService.ClaimDbName, dbName),
            new(PublicTokenService.ClaimBranchId, branchId.ToString()),
            new(PublicTokenService.ClaimPurpose, PublicTokenService.PurposePortalEntry),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        return Sign(claims, DateTime.UtcNow.AddDays(EntryDays));
    }

    /// <summary>OTP doğrulandıktan sonra üretilen MÜŞTERİ-SCOPE oturum token'ı (customerId gömülü, kısa ömür).</summary>
    public string CreateSessionToken(string companyCode, string dbName, int branchId, int customerId)
    {
        var claims = new List<Claim>
        {
            new(PublicTokenService.ClaimCompanyCode, companyCode),
            new(PublicTokenService.ClaimDbName, dbName),
            new(PublicTokenService.ClaimBranchId, branchId.ToString()),
            new(PublicTokenService.ClaimCustomerId, customerId.ToString()),
            new(PublicTokenService.ClaimPurpose, PublicTokenService.PurposePortal),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        return Sign(claims, DateTime.UtcNow.AddMinutes(SessionMinutes));
    }

    /// <summary>Portal GİRİŞ token'ını doğrular (purpose=portal-entry). Geçersizse null.</summary>
    public PortalEntryContext? ValidateEntry(string? token)
    {
        var principal = ValidatePrincipal(token);
        if (principal is null) return null;
        if (principal.FindFirst(PublicTokenService.ClaimPurpose)?.Value != PublicTokenService.PurposePortalEntry) return null;
        var code = principal.FindFirst(PublicTokenService.ClaimCompanyCode)?.Value;
        var db = principal.FindFirst(PublicTokenService.ClaimDbName)?.Value;
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(db)) return null;
        if (!int.TryParse(principal.FindFirst(PublicTokenService.ClaimBranchId)?.Value, out var branch) || branch <= 0) return null;
        return new PortalEntryContext(code, db, branch);
    }

    /// <summary>Müşteri oturum token'ını doğrular (purpose=portal). Geçersizse null.</summary>
    public PortalContext? ValidateSession(string? token)
    {
        var principal = ValidatePrincipal(token);
        if (principal is null) return null;
        if (principal.FindFirst(PublicTokenService.ClaimPurpose)?.Value != PublicTokenService.PurposePortal) return null;
        var code = principal.FindFirst(PublicTokenService.ClaimCompanyCode)?.Value;
        var db = principal.FindFirst(PublicTokenService.ClaimDbName)?.Value;
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(db)) return null;
        if (!int.TryParse(principal.FindFirst(PublicTokenService.ClaimBranchId)?.Value, out var branch) || branch <= 0) return null;
        if (!int.TryParse(principal.FindFirst(PublicTokenService.ClaimCustomerId)?.Value, out var cust) || cust <= 0) return null;
        return new PortalContext(code, db, branch, cust);
    }
}

/// <summary>FAZ 5a — Portal GİRİŞ bağlamı (şirket+şube sabit, müşteri henüz doğrulanmadı).</summary>
public sealed record PortalEntryContext(string CompanyCode, string DbName, int BranchId);

/// <summary>FAZ 5a — Doğrulanmış müşteri portal oturumu (şirket+şube+müşteri sabit; müşteri değiştiremez).</summary>
public sealed record PortalContext(string CompanyCode, string DbName, int BranchId, int CustomerId);
