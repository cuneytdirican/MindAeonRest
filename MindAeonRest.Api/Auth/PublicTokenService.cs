using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MindAeonRest.Api.Auth;

/// <summary>Anonim (müşteri) erişimi için KISITLI-SCOPE JWT üretir/doğrular — QR masadan sipariş, public rezervasyon
/// (sonraki fazlar) için ORTAK altyapı. Normal oturum JWT'sinden farkı: <b>ayrı audience</b> (MindAeonRest.Public) taşır,
/// böylece JwtBearer (normal /api [Authorize]) bu token'ı REDDEDER → public token yalnız [AllowAnonymous] public uçlarda,
/// elle doğrulanarak kullanılabilir (yetki yükselmesi yok). Token'a şube+masa GÖMÜLÜDÜR; müşteri bu kapsamın dışına
/// çıkamaz (IDOR imkânsız). İmza anahtarı normal JWT ile AYNI kaynaktan (JwtKeyProvider) çözülür.</summary>
public class PublicTokenService(IConfiguration config)
{
    public const string ClaimCompanyCode = "company_code";
    public const string ClaimDbName = "db";
    public const string ClaimBranchId = "branch";
    public const string ClaimTableId = "table";
    public const string ClaimCustomerId = "customer";
    public const string ClaimOrderId = "order";
    public const string ClaimPurpose = "purpose";

    public const string PurposeOrder = "public-order";
    // FAZ 5d Geri-bildirim hunisi: adisyon kapanınca/ödeme sonrası üretilen ANKET token'ı (şube + opsiyonel adisyon
    // gömülü, masasız). Public anket sayfası bunu taşır; müşteri yalnız kendi şube kapsamında geri bildirim verir.
    public const string PurposeFeedback = "public-feedback";
    // FAZ 5a Sadakat Portalı: restoran tarafından üretilen GİRİŞ token'ı (şube+şirket gömülü, masasız) →
    // müşteri telefon+OTP ile giriş yapmadan ÖNCE bağlamı taşır (şirket kodu URL'de AÇIK GEÇMEZ → enumerasyon yok).
    public const string PurposePortalEntry = "portal-entry";
    // OTP doğrulandıktan sonra üretilen MÜŞTERİ-SCOPE oturum token'ı (customerId gömülü, kısa ömür).
    public const string PurposePortal = "portal";
    // FAZ 5c Online Rezervasyon + Bekleme Listesi: şube gömülü (masasız) public token → public rezervasyon sayfası
    // bunu taşır; müşteri yalnız kendi şube kapsamında slot sorgular / rezervasyon oluşturur / bekleme listesine girer.
    public const string PurposeReserve = "public-reserve";

    // AYRI audience: normal JwtBearer doğrulaması (ValidAudience=Jwt:Audience) bu token'ı kabul ETMEZ.
    private const string PublicAudience = "MindAeonRest.Public";

    private string Key => JwtKeyProvider.Resolve(config);
    private string Issuer => config["Jwt:Issuer"] ?? "MindAeonRest.Api";
    /// <summary>QR kodları basılıp masaya yapıştırıldığından token uzun ömürlüdür; süre config ile ayarlanır
    /// (Public:TokenDays, varsayılan 180 gün). İptal/rotasyon: masaya yeni QR üret → eski token süre bitince düşer.</summary>
    private int TokenDays => int.TryParse(config["Public:TokenDays"], out var d) && d > 0 ? d : 180;

    /// <summary>Bir masa için imzalı, şube+masa gömülü public token üretir (back-office QR ekranı).</summary>
    public string CreateOrderToken(string companyCode, string dbName, int branchId, int tableId)
    {
        var expires = DateTime.UtcNow.AddDays(TokenDays);
        var claims = new List<Claim>
        {
            new(ClaimCompanyCode, companyCode),
            new(ClaimDbName, dbName),
            new(ClaimBranchId, branchId.ToString()),
            new(ClaimTableId, tableId.ToString()),
            new(ClaimPurpose, PurposeOrder),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, PublicAudience, claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Public token'ı doğrular (imza + issuer + audience + süre + purpose). Geçersizse null.</summary>
    public PublicContext? Validate(string? token, string expectedPurpose = PurposeOrder)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Issuer,
                ValidAudience = PublicAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            if (principal.FindFirst(ClaimPurpose)?.Value != expectedPurpose) return null;
            var code = principal.FindFirst(ClaimCompanyCode)?.Value;
            var db = principal.FindFirst(ClaimDbName)?.Value;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(db)) return null;
            if (!int.TryParse(principal.FindFirst(ClaimBranchId)?.Value, out var branch) || branch <= 0) return null;
            if (!int.TryParse(principal.FindFirst(ClaimTableId)?.Value, out var table) || table <= 0) return null;
            return new PublicContext(code, db, branch, table);
        }
        catch { return null; }
    }

    /// <summary>FAZ 5d — Şube (+ opsiyonel adisyon) gömülü ANKET token'ı üretir (masasız). Anket linki bunu taşır.</summary>
    public string CreateFeedbackToken(string companyCode, string dbName, int branchId, int orderId = 0)
    {
        var expires = DateTime.UtcNow.AddDays(TokenDays);
        var claims = new List<Claim>
        {
            new(ClaimCompanyCode, companyCode),
            new(ClaimDbName, dbName),
            new(ClaimBranchId, branchId.ToString()),
            new(ClaimOrderId, orderId.ToString()),
            new(ClaimPurpose, PurposeFeedback),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, PublicAudience, claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Anket token'ını doğrular (imza + issuer + audience + süre + purpose=feedback). Geçersizse null.
    /// Masa gerektirmez; adisyon opsiyoneldir (0 = adisyona bağlı değil).</summary>
    public FeedbackTokenContext? ValidateFeedback(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Issuer,
                ValidAudience = PublicAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            if (principal.FindFirst(ClaimPurpose)?.Value != PurposeFeedback) return null;
            var code = principal.FindFirst(ClaimCompanyCode)?.Value;
            var db = principal.FindFirst(ClaimDbName)?.Value;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(db)) return null;
            if (!int.TryParse(principal.FindFirst(ClaimBranchId)?.Value, out var branch) || branch <= 0) return null;
            int.TryParse(principal.FindFirst(ClaimOrderId)?.Value, out var order);   // opsiyonel; 0 kabul
            return new FeedbackTokenContext(code, db, branch, order > 0 ? order : null);
        }
        catch { return null; }
    }
    /// <summary>FAZ 5c — Şube gömülü ONLINE REZERVASYON token'ı üretir (masasız). Public rezervasyon linki bunu taşır.</summary>
    public string CreateReserveToken(string companyCode, string dbName, int branchId)
    {
        var expires = DateTime.UtcNow.AddDays(TokenDays);
        var claims = new List<Claim>
        {
            new(ClaimCompanyCode, companyCode),
            new(ClaimDbName, dbName),
            new(ClaimBranchId, branchId.ToString()),
            new(ClaimPurpose, PurposeReserve),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, PublicAudience, claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Rezervasyon token'ını doğrular (imza + issuer + audience + süre + purpose=public-reserve). Geçersizse null.
    /// Masa gerektirmez; yalnız şube kapsamı taşır.</summary>
    public ReserveTokenContext? ValidateReserve(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Issuer,
                ValidAudience = PublicAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            if (principal.FindFirst(ClaimPurpose)?.Value != PurposeReserve) return null;
            var code = principal.FindFirst(ClaimCompanyCode)?.Value;
            var db = principal.FindFirst(ClaimDbName)?.Value;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(db)) return null;
            if (!int.TryParse(principal.FindFirst(ClaimBranchId)?.Value, out var branch) || branch <= 0) return null;
            return new ReserveTokenContext(code, db, branch);
        }
        catch { return null; }
    }
}

/// <summary>Doğrulanmış public token bağlamı — şube+masa sabittir (müşteri değiştiremez).</summary>
public sealed record PublicContext(string CompanyCode, string DbName, int BranchId, int TableId);

/// <summary>Doğrulanmış online rezervasyon token bağlamı — şube sabit (müşteri değiştiremez).</summary>
public sealed record ReserveTokenContext(string CompanyCode, string DbName, int BranchId);

/// <summary>Doğrulanmış anket token bağlamı — şube sabit; adisyon opsiyonel (müşteri değiştiremez).</summary>
public sealed record FeedbackTokenContext(string CompanyCode, string DbName, int BranchId, int? OrderId);
