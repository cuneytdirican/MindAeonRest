using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    UserInactive,
    CompanyRequired,
    CompanyNotFound,
    CompanyNeedsSetup,   // Şirket kaydı yok; giriş yapan SysAdmin -> kurulum sayfası
    BranchRequired,
    BranchNotFound,
    AccessDenied,
    Error
}

public class LoginOutcome
{
    public LoginStatus Status { get; set; }
    public string? Message { get; set; }
    public User? User { get; set; }
    public Company? Company { get; set; }
    public CompanyBranch? Branch { get; set; }
    public List<CompanyBranch> AllowedBranches { get; set; } = new();
    public string NormalizedCompanyCode { get; set; } = string.Empty;
}

/// <summary>
/// Tüm giriş akışını yönetir: kimlik doğrulama -> şirket çözümleme -> DB hazırlama -> erişim kontrolü.
/// </summary>
public class LoginService
{
    private readonly AuthService _auth = new();
    private readonly CompanyService _companies = new();
    private readonly BranchService _branches = new();
    private readonly UserAccessService _access = new();

    public LoginOutcome Login(string companyCode, int? branchId, string username, string password)
    {
        var outcome = new LoginOutcome
        {
            NormalizedCompanyCode = CodeHelper.Normalize(companyCode)
        };

        // 1) Kimlik doğrulama
        var authResult = _auth.Authenticate(username, password, out var user);
        if (authResult == AuthService.AuthResult.InvalidCredentials)
            return Fail(outcome, LoginStatus.InvalidCredentials, "Kullanıcı adı veya parola hatalı.");
        if (authResult == AuthService.AuthResult.UserInactive)
            return Fail(outcome, LoginStatus.UserInactive, "Bu kullanıcı pasif durumda.");

        outcome.User = user!;
        bool isSysAdmin = user!.RoleName.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase);

        // 2) Şirket kodu zorunlu
        if (outcome.NormalizedCompanyCode.Length == 0)
            return Fail(outcome, LoginStatus.CompanyRequired, "Lütfen şirket kodunu giriniz.");

        // 3) Şirketi çöz
        var company = _companies.GetByCode(outcome.NormalizedCompanyCode);
        if (company is null)
        {
            // Şirket yok: SysAdmin ise kurulum sayfasına yönlendir
            return isSysAdmin
                ? Fail(outcome, LoginStatus.CompanyNeedsSetup,
                       $"'{outcome.NormalizedCompanyCode}' şirketi bulunamadı. Kurulum başlatılabilir.")
                : Fail(outcome, LoginStatus.CompanyNotFound,
                       "Şirket bulunamadı. Lütfen yöneticinize başvurun.");
        }
        outcome.Company = company;

        // 3b) Pasif şirkete giriş engeli: NON-SysAdmin reddedilir; SysAdmin yönetim/yeniden-aktifleştirme için girebilir.
        if (!company.IsActive && !isSysAdmin)
            return Fail(outcome, LoginStatus.AccessDenied, "Şirket pasif - giriş kapalı.");

        // 4) Şirket veritabanını hazırla (yoksa oluştur + şema/migration)
        try
        {
            CompanyDatabase.Provision(company);
        }
        catch (Exception ex)
        {
            return Fail(outcome, LoginStatus.Error, $"Şirket veritabanı hazırlanamadı: {ex.Message}");
        }

        // 5) Şube + erişim kontrolü
        if (isSysAdmin)
        {
            // SysAdmin her şubeye girebilir AMA şube bu şirkete ait olmalı (çapraz-şirket şube damgası engeli).
            if (branchId.HasValue)
            {
                var b = _branches.GetById(branchId.Value);
                // Pasif şubede işlem yok: SysAdmin dahil aktif olmayan şubeye giriş reddedilir.
                if (b is null || b.CompanyId != company.Id || !b.IsActive)
                    return Fail(outcome, LoginStatus.BranchNotFound, "Seçilen şube pasif veya geçersiz.");
                outcome.Branch = b;
            }
            else outcome.Branch = _branches.GetByCompany(company.Id).FirstOrDefault();
        }
        else
        {
            if (!branchId.HasValue)
            {
                // Kimlik geçerli; kullanıcının erişebildiği şubeleri doldur (API şube seçimi için).
                var active = _branches.GetActiveByCompany(company.Id);
                var access = _access.GetAccess(user.Id, company.Id);
                outcome.AllowedBranches = access.AllBranches
                    ? active
                    : active.Where(b => access.BranchIds.Contains(b.Id)).ToList();
                return Fail(outcome, LoginStatus.BranchRequired, "Lütfen şube seçiniz.");
            }

            var branch = _branches.GetById(branchId.Value);
            // Pasif şubede işlem yok: aktif olmayan şubeye giriş reddedilir.
            if (branch is null || branch.CompanyId != company.Id || !branch.IsActive)
                return Fail(outcome, LoginStatus.BranchNotFound, "Seçilen şube pasif veya geçersiz.");

            if (!_access.HasAccess(user.Id, company.Id, branch.Id))
                return Fail(outcome, LoginStatus.AccessDenied,
                    "Bu şirket/şubeye giriş yetkiniz yok. Lütfen yöneticinize başvurun.");

            outcome.Branch = branch;
        }

        // 6) Başarılı
        _auth.UpdateLastLogin(user.Id);
        outcome.Status = LoginStatus.Success;
        return outcome;
    }

    private static LoginOutcome Fail(LoginOutcome o, LoginStatus status, string message)
    {
        o.Status = status;
        o.Message = message;
        return o;
    }
}
