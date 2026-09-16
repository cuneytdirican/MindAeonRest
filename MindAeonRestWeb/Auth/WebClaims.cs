namespace MindAeonRestWeb.Auth;

/// <summary>Cookie oturumunda taşınan claim adları — API/JWT ile BİREBİR aynı string değerler
/// (JwtTokenService.Claim* ile eşleşir). Böylece WebTenantMiddleware, TenantMiddleware ile aynı mantıkla okur.</summary>
public static class WebClaims
{
    public const string CompanyCode = "company_code";
    public const string CompanyId = "company_id";
    public const string DbName = "db";
    public const string BranchId = "branch";
    public const string KioskNo = "kiosk";
    public const string FeatureBits = "features";
    public const string Package = "package";

    // Gerçek lisans LİMİTLERİ + expiry (FIX C2/C3) — API JWT ile BİREBİR aynı adlar (paylaşımlı sözleşme).
    // Böylece cookie/PWA token yeniden-girişte doğru limitleri/expiry'yi taşır; add-on limitleri kaybolmaz.
    public const string LicMaxUsers = "lic_mu";      // MaxUsers (string)
    public const string LicMaxBranches = "lic_mb";   // MaxBranches (string)
    public const string LicMaxKiosks = "lic_mk";     // MaxKiosks (string)
    public const string LicExpires = "lic_exp";      // ExpiresUtc, ISO-8601 round-trip ("o"); süresiz → claim atlanır

    // SysAdmin oturum-içi kiracı geçişi (S3 "Bu şirkete geç"). Yalnız web/cookie tarafında anlamlı.
    public const string SysAdminHome = "sa_home";    // SysAdmin'in ORİJİNAL (ev) şirket kodu — geri dönüş için
    public const string Impersonating = "sa_imp";    // "1" iken hedef kiracıda geçiş modundayız
}
