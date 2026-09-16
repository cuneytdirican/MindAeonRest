using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Auth;

namespace MindAeonRestWeb.Services;

/// <summary>Web (cookie) girişini in-process orkestre eder — JWT YOK, LoginService'i DEĞİŞTİRMEDEN ve
/// CompanyDatabase.Provision'ı ÇAĞIRMADAN (public web'de otomatik DB üretimi yok; yalnız mevcut kiracıya giriş).
/// Enumerasyona kapalı: tüm hatalarda tek-tip mesaj. Master DB servisleri Session gerektirmez (OpenConnection).</summary>
public class WebLoginService(IMemoryCache cache)
{
    private readonly AuthService _auth = new();
    private readonly CompanyService _companies = new();
    private readonly BranchService _branches = new();
    private readonly UserAccessService _access = new();
    private readonly UserRoleService _roles = new();
    private readonly LicenseService _license = new();
    private readonly TwoFactorService _twoFactor = new();

    public const string GenericError = "Şirket kodu, kullanıcı adı veya parola hatalı.";
    private const int MaxFails = 5;
    private static readonly TimeSpan LockWindow = TimeSpan.FromMinutes(10);

    public sealed record BranchOption(int Id, string Name);

    public sealed class StageResult
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public bool Locked { get; init; }
        public User? User { get; init; }
        public Company? Company { get; init; }
        public List<string> Roles { get; init; } = new();
        public LicenseStatus? License { get; init; }
        public List<BranchOption> Branches { get; init; } = new();
        public bool IsSysAdmin { get; init; }
        /// <summary>Kullanıcı 2FA'yı etkinleştirmişse true → login akışı cookie SignIn'den ÖNCE TOTP challenge ister.</summary>
        public bool TwoFactorEnabled { get; init; }
    }

    private static StageResult Fail(bool locked = false) =>
        new() { Ok = false, Error = locked ? "Çok fazla başarısız deneme. Lütfen birkaç dakika sonra tekrar deneyin." : GenericError, Locked = locked };

    /// <summary>1. aşama: kimlik + şirket + erişilebilir şube listesi. Başarısızsa tek-tip hata (enumerasyon yok).</summary>
    public StageResult Authenticate(string? companyCode, string? username, string? password)
    {
        string code = CodeHelper.Normalize(companyCode);
        string user = (username ?? "").Trim();
        string lockKey = $"loginfail:{code}:{user.ToLowerInvariant()}";

        if (cache.TryGetValue<int>(lockKey, out var fails) && fails >= MaxFails)
            return Fail(locked: true);

        StageResult Bad()
        {
            int n = (cache.TryGetValue<int>(lockKey, out var f) ? f : 0) + 1;
            cache.Set(lockKey, n, LockWindow);
            return Fail();
        }

        if (code.Length == 0 || user.Length == 0 || string.IsNullOrEmpty(password)) return Bad();

        // Kimlik (master DB, Session gerektirmez)
        var auth = _auth.Authenticate(user, password!, out var u);
        if (auth != AuthService.AuthResult.Success || u is null) return Bad();

        // Şirket (public web'de Provision YOK; yalnız mevcut kiracı)
        var company = _companies.GetByCode(code);
        if (company is null) return Bad();

        var roles = _roles.GetRoleNames(u.Id);
        if (roles.Count == 0) roles = new List<string> { u.RoleName };
        bool isSysAdmin = roles.Any(r => r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase));

        // Pasif şirkete yalnız SysAdmin girebilir (yönetmek/aktifleştirmek için);
        // diğer tüm kullanıcılar kilitlenir (WPF LoginService ile parite). Enumerasyona kapalı tek-tip hata.
        if (!company.IsActive && !isSysAdmin) return Bad();

        // Erişilebilir şubeler
        var active = _branches.GetActiveByCompany(company.Id);
        List<CompanyBranch> allowed;
        if (isSysAdmin) allowed = active;
        else
        {
            var acc = _access.GetAccess(u.Id, company.Id);
            allowed = acc.AllBranches ? active : active.Where(b => acc.BranchIds.Contains(b.Id)).ToList();
        }
        if (allowed.Count == 0) return Bad();   // erişim yok → yine tek-tip

        // Lisans: web/merkezî, donanım bağı YOK → CompanyCode bazlı paket/özellik
        var lic = _license.GetStatusByCompany(company.Code);
        // Lisans DURUMUNU uygula (WPF LoginWindow ile parite; API AuthController da 403 döner):
        // süresi dolmuş/geçersiz lisansla giriş engellenir — aksi halde web'de tam paket sızardı.
        if (!lic.IsUsable)
            return new StageResult { Ok = false, Error = "Lisansınız geçerli değil (" + lic.StateText + "). Lütfen bayinize/yöneticinize başvurun." };

        cache.Remove(lockKey);   // başarı → sayaç sıfır
        _auth.UpdateLastLogin(u.Id);

        return new StageResult
        {
            Ok = true, User = u, Company = company, Roles = roles, License = lic, IsSysAdmin = isSysAdmin,
            TwoFactorEnabled = _twoFactor.IsEnabled(u.Id),
            Branches = allowed.Select(b => new BranchOption(b.Id, b.Name)).ToList()
        };
    }

    /// <summary>Pre-auth sonrası (şube seçimi / 2FA) cookie için lisansı yeniden kurar. Pre-auth zarfı yalnız paket+özellik
    /// taşır; eskiden LicenseStatus varsayılanları (MaxUsers=3, MaxBranches=1, bitiş yok) cookie'ye yazılıyordu →
    /// çok şubeli/2FA'lı kiracı yöneticisi kullanıcı/şube ekleyemiyordu. Gerçek limitler sistem DB'den okunur;
    /// okunamazsa eski davranışa düşülür.</summary>
    public static LicenseStatus ReloadLicense(Pages.LoginModel.PreAuth pre)
    {
        try
        {
            var lic = new LicenseService().GetStatusByCompany(pre.CompanyCode);
            if (lic.IsUsable) return lic;
        }
        catch { /* sistem DB okunamadı → aşağıdaki geriye-uyumlu kurulum */ }
        Enum.TryParse<LicensePackage>(pre.Package, out var pkg);
        return new LicenseStatus { State = LicenseState.Licensed, Package = pkg, Features = (Feature)pre.Features };
    }

    /// <summary>Seçilen şubeye erişimi doğrular (2. aşama güvenliği).</summary>
    public bool VerifyBranch(int userId, int companyId, int branchId, bool isSysAdmin)
    {
        var branch = _branches.GetById(branchId);
        // Sadece AKTİF şubelere işlem: pasif şube seçimi/oturumu reddedilir (FIX C1).
        if (branch is null || branch.CompanyId != companyId || !branch.IsActive) return false;
        return isSysAdmin || _access.HasAccess(userId, companyId, branchId);
    }

    /// <summary>Cookie oturumu için ClaimsPrincipal — WebTenantMiddleware'in okuduğu tüm claim'ler (kiosk=0).
    /// <paramref name="sysAdminHomeCode"/> doluysa geri-dönüş için ev şirket kodu, <paramref name="impersonating"/>
    /// true ise SysAdmin geçiş modu claim'i eklenir (S3). Mevcut çağıranlar için varsayılanlar geriye-uyumlu.</summary>
    public static ClaimsPrincipal BuildPrincipal(User user, Company company, int branchId, IReadOnlyList<string> roles, LicenseStatus lic,
        string? sysAdminHomeCode = null, bool impersonating = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("name", user.FullName ?? user.Username),
            new(WebClaims.CompanyCode, company.Code),
            new(WebClaims.CompanyId, company.Id.ToString()),
            new(WebClaims.DbName, company.DatabaseName),
            new(WebClaims.BranchId, branchId.ToString()),
            new(WebClaims.KioskNo, "0"),
            new(WebClaims.FeatureBits, ((long)lic.Features).ToString()),
            new(WebClaims.Package, lic.Package.ToString()),
            // Gerçek lisans limitleri (add-on dahil) cookie'ye yazılır → middleware paket-tabanından tahmin etmez (FIX C2).
            new(WebClaims.LicMaxUsers, lic.MaxUsers.ToString()),
            new(WebClaims.LicMaxBranches, lic.MaxBranches.ToString()),
            new(WebClaims.LicMaxKiosks, lic.MaxKiosks.ToString()),
        };
        // Süresiz lisansta ExpiresUtc null → claim eklenmez (geriye-uyum ile aynı: yok sayılır).
        if (lic.ExpiresUtc is { } exp)
            claims.Add(new(WebClaims.LicExpires, exp.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture)));
        // SysAdmin geçiş (S3): ev şirket kodu ve geçiş bayrağı yalnız gerekliyse yazılır (aksi halde normal oturum).
        if (!string.IsNullOrWhiteSpace(sysAdminHomeCode))
            claims.Add(new(WebClaims.SysAdminHome, sysAdminHomeCode));
        if (impersonating)
            claims.Add(new(WebClaims.Impersonating, "1"));
        foreach (var r in roles) claims.Add(new(ClaimTypes.Role, r));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "name", ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Role göre açılış: yönetim (SysAdmin/Patron/Yönetici — Session.IsTenantAdmin ile aynı küme) → back-office;
    /// POS rolleri → PWA. Provisioning ilk yöneticiyi "Yönetici" rolüyle açar → o da back-office'e düşmeli.</summary>
    public static string Landing(IReadOnlyList<string> roles)
    {
        bool Has(string r) => roles.Any(x => x.Equals(r, StringComparison.OrdinalIgnoreCase));
        if (Has("SysAdmin") || Has("Patron") || Has("Yönetici") || Has("Yonetici")) return "backoffice";
        return "pos";
    }
}
