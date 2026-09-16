using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Api.Auth;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(JwtTokenService jwt, IConfiguration config) : ControllerBase
{
    private readonly UserRoleService _roles = new();
    private readonly LoginService _login = new();
    private readonly LicenseService _license = new();

    /// <summary>Kullanıcı + parola ile giriş → JWT. Lisans paketine ve role göre açılış ekranı döner.
    /// Public web/SaaS için enumerasyon-güvenli: şirket/kullanıcı/şube listeleri sızdırılmaz;
    /// şube seçimi yalnızca kimlik doğrulandıktan sonra döner.</summary>
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest req)
    {
        var outcome = _login.Login(req.CompanyCode, req.BranchId, req.Username, req.Password);

        // Kimlik geçerli ama şube seçimi gerekli → şube listesini döndür (token YOK).
        if (outcome.Status == LoginStatus.BranchRequired)
            return Ok(new
            {
                needBranch = true,
                branches = outcome.AllowedBranches.Select(b => new BranchDto(b.Id, b.Name))
            });

        // Başarısız TÜM durumlar (CompanyNeedsSetup dahil) → UNIFORM mesaj; hangi kısmın hatalı olduğunu sızdırma.
        if (outcome.Status != LoginStatus.Success || outcome.User is null || outcome.Company is null)
            return Unauthorized(new { message = "Şirket kodu, kullanıcı adı veya parola hatalı." });

        var user = outcome.User;
        var company = outcome.Company;
        var branch = outcome.Branch;
        var roles = _roles.GetRoleNames(user.Id);
        if (roles.Count == 0) roles = new List<string> { user.RoleName };

        // Lisans çözümü:
        //  • LAN tek-başına terminal (varsayılan): donanım-bazlı → bu makinenin terminal lisansı.
        //  • Merkezî host (Licensing:CentralHost=true; PWA/MAUI bulut girişi): şirket-bazlı →
        //    müşterinin GERÇEK şirket lisansı (paket/özellik/limit/expiry), bulut donanımı değil.
        bool central = config.GetValue<bool>("Licensing:CentralHost");
        var lic = central
            ? _license.GetStatusByCompany(company.Code)
            : _license.GetStatus(company.Code, HardwareId.Current);
        if (!lic.IsUsable)
            return StatusCode(403, new { message = "Lisans geçersiz veya süresi dolmuş: " + lic.StateText });

        var (token, expires) = jwt.Create(user, company, branch, roles,
            kioskNo: MindAeonRest.Helpers.AppConfig.KioskNo, featureBits: (long)lic.Features, package: lic.Package.ToString(), lic: lic);

        return Ok(new LoginResponse(
            token, expires, user.Id, user.FullName, company.Code, company.Name,
            branch?.Id, branch?.Name, roles, Landing(roles), lic.Package.ToString(), (long)lic.Features));
    }

    /// <summary>Oturumdaki kullanıcının erişebildiği şubeler (yetkili; enumerasyon değil — kendi kiracısı).
    /// Patron paneli şube filtresi için. TenantMiddleware Session'ı doldurur.</summary>
    [Authorize]
    [HttpGet("my-branches")]
    public ActionResult<IEnumerable<BranchDto>> MyBranches()
    {
        var code = Session.CurrentCompany?.Code;
        var userId = Session.CurrentUser?.Id;
        if (string.IsNullOrEmpty(code) || userId is null) return Unauthorized();
        var company = new CompanyService().GetByCode(code);
        if (company is null) return NotFound();

        var branches = new BranchService().GetActiveByCompany(company.Id);
        if (!Session.IsSysAdmin)
        {
            var acc = new UserAccessService().GetAccess(userId.Value, company.Id);
            if (!acc.AllBranches) branches = branches.Where(b => acc.BranchIds.Contains(b.Id)).ToList();
        }
        return Ok(branches.Select(b => new BranchDto(b.Id, b.Name)));
    }

    private static string Landing(IReadOnlyList<string> roles)
    {
        bool Has(string r) => roles.Any(x => x.Equals(r, StringComparison.OrdinalIgnoreCase));
        if (Has("SysAdmin")) return "patron";
        if (Has("Mutfak")) return "kitchen";
        return "waiter";
    }
}
